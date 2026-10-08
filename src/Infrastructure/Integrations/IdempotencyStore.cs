using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Integrations;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Integrations;

public sealed class IdempotencyStore : IIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IDataRootResolver _dataRoot;
    private readonly IIntegrationConfigService _config;
    private readonly ILogger<IdempotencyStore> _logger;
    private readonly object _gate = new();

    public IdempotencyStore(IDataRootResolver dataRoot, IIntegrationConfigService config, ILogger<IdempotencyStore> logger)
    {
        _dataRoot = dataRoot;
        _config = config;
        _logger = logger;
    }

    public IdempotencyLookupResult TryGet(string scopeKey, string key, string fingerprint)
    {
        lock (_gate)
        {
            var path = FilePath(scopeKey, key);
            if (!File.Exists(path)) return new IdempotencyLookupResult { Found = false };
            try
            {
                var rec = JsonSerializer.Deserialize<Record>(File.ReadAllText(path), JsonOpts);
                if (rec is null) return new IdempotencyLookupResult { Found = false };
                if (!string.Equals(rec.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return new IdempotencyLookupResult { Found = true, FingerprintMismatch = true };
                return new IdempotencyLookupResult
                {
                    Found = true,
                    StatusCode = rec.StatusCode,
                    ResponseJson = rec.ResponseJson
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Idempotency read failed");
                return new IdempotencyLookupResult { Found = false };
            }
        }
    }

    public void Put(string scopeKey, string key, string fingerprint, int statusCode, string responseJson, TimeProvider time)
    {
        lock (_gate)
        {
            var path = FilePath(scopeKey, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var rec = new Record
            {
                Fingerprint = fingerprint,
                StatusCode = statusCode,
                ResponseJson = responseJson,
                StoredAt = time.GetUtcNow()
            };
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(rec, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
    }

    public void Prune(TimeProvider time)
    {
        lock (_gate)
        {
            var dir = Dir();
            if (!Directory.Exists(dir)) return;
            var hours = _config.Get().IdempotencyRetentionHours;
            var cutoff = time.GetUtcNow().AddHours(-hours);
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var rec = JsonSerializer.Deserialize<Record>(File.ReadAllText(file), JsonOpts);
                    if (rec is null || rec.StoredAt < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }

    private string FilePath(string ns, string key)
    {
        var safeNs = HashName(ns);
        var safeKey = HashName(key);
        return Path.Combine(Dir(), safeNs, safeKey + ".json");
    }

    private string Dir() => Path.Combine(_dataRoot.ResolveDataRoot(), "integrations", "idempotency");

    private static string HashName(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32].ToLowerInvariant();

    private sealed class Record
    {
        public string Fingerprint { get; set; } = "";
        public int StatusCode { get; set; }
        public string ResponseJson { get; set; } = "";
        public DateTimeOffset StoredAt { get; set; }
    }
}
