using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Secrets;

public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<DpapiSecretStore> _logger;
    private readonly object _gate = new();

    public DpapiSecretStore(IDataRootResolver dataRoot, ILogger<DpapiSecretStore> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public bool TryGet(string key, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate)
        {
            var store = Load();
            if (!store.Entries.TryGetValue(key, out var entry) || string.IsNullOrEmpty(entry.CipherBase64))
                return false;
            try
            {
                value = Unprotect(entry.CipherBase64);
                return value is not null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect secret {Key}", key);
                return false;
            }
        }
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key is required.", nameof(key));
        lock (_gate)
        {
            var store = Load();
            store.Entries[key] = new SecretEntry
            {
                CipherBase64 = Protect(value ?? ""),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            Save(store);
        }
    }

    public bool Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate)
        {
            var store = Load();
            if (!store.Entries.Remove(key)) return false;
            Save(store);
            return true;
        }
    }

    public bool Has(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        lock (_gate)
        {
            var store = Load();
            return store.Entries.ContainsKey(key) && !string.IsNullOrEmpty(store.Entries[key].CipherBase64);
        }
    }

    public IReadOnlyDictionary<string, string> ExportPortable()
    {
        lock (_gate)
        {
            var store = Load();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, entry) in store.Entries)
            {
                if (string.IsNullOrEmpty(entry.CipherBase64)) continue;
                try
                {
                    var plain = Unprotect(entry.CipherBase64);
                    if (plain is not null)
                        dict[key] = plain;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping secret {Key} during portable export", key);
                }
            }
            return dict;
        }
    }

    public void ImportPortable(IReadOnlyDictionary<string, string> values, bool overwriteExisting = true)
    {
        if (values is null || values.Count == 0) return;
        lock (_gate)
        {
            var store = Load();
            foreach (var (key, value) in values)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!overwriteExisting && store.Entries.ContainsKey(key)) continue;
                store.Entries[key] = new SecretEntry
                {
                    CipherBase64 = Protect(value ?? ""),
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
            Save(store);
        }
    }

    public int ImportPortableFileIfPresent()
    {
        var dataRoot = _dataRoot.ResolveDataRoot();
        var candidates = new[]
        {
            Path.Combine(dataRoot, "secrets-portable.json"),
            Path.Combine(dataRoot, "secrets", "secrets-portable.json")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                Dictionary<string, string>? map = null;
                if (root.TryGetProperty("secrets", out var secretsEl) && secretsEl.ValueKind == JsonValueKind.Object)
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in secretsEl.EnumerateObject())
                        map[p.Name] = p.Value.GetString() ?? "";
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in root.EnumerateObject())
                    {
                        if (p.NameEquals("kind") || p.NameEquals("createdUtc")) continue;
                        if (p.Value.ValueKind == JsonValueKind.String)
                            map[p.Name] = p.Value.GetString() ?? "";
                    }
                }

                if (map is { Count: > 0 })
                    ImportPortable(map, overwriteExisting: true);

                try { File.Delete(path); } catch { /* best effort */ }
                _logger.LogInformation("Imported {Count} portable secrets from {Path} and removed the file", map?.Count ?? 0, path);
                return map?.Count ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import portable secrets from {Path}", path);
            }
        }

        return 0;
    }

    private SecretFile Load()
    {
        var path = StorePath();
        if (!File.Exists(path))
            return new SecretFile();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SecretFile>(json, JsonOpts) ?? new SecretFile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read secrets store");
            return new SecretFile();
        }
    }

    private void Save(SecretFile store)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(store, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "secrets", "secrets.json");

    private static string Protect(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        // Non-Windows fallback for unit tests only — not for production targets.
        return Convert.ToBase64String(bytes);
    }

    private static string? Unprotect(string cipherBase64)
    {
        var bytes = Convert.FromBase64String(cipherBase64);
        if (OperatingSystem.IsWindows())
        {
            var plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class SecretFile
    {
        public Dictionary<string, SecretEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SecretEntry
    {
        public string CipherBase64 { get; set; } = "";
        public DateTimeOffset? UpdatedUtc { get; set; }
    }
}
