using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

/// <summary>
/// DPAPI-protected cloud OAuth credentials. Never part of <c>ISecretStore</c> / ExportPortable / Move Kits.
/// </summary>
public sealed class DpapiCloudCredentialStore : ICloudCredentialStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<DpapiCloudCredentialStore> _logger;
    private readonly object _gate = new();

    public DpapiCloudCredentialStore(IDataRootResolver dataRoot, ILogger<DpapiCloudCredentialStore> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public bool TryGet(CloudProviderKind provider, out CloudCredentialEnvelope? credential)
    {
        credential = null;
        lock (_gate)
        {
            var store = Load();
            if (!store.Credentials.TryGetValue(Key(provider), out var env) || env is null)
                return false;
            credential = Clone(env);
            return true;
        }
    }

    public void Set(CloudProviderKind provider, CloudCredentialEnvelope credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        lock (_gate)
        {
            var store = Load();
            var copy = Clone(credential);
            copy.Provider = provider;
            copy.UpdatedUtc = DateTimeOffset.UtcNow;
            store.Credentials[Key(provider)] = copy;
            Save(store);
        }
    }

    public bool Remove(CloudProviderKind provider)
    {
        lock (_gate)
        {
            var store = Load();
            if (!store.Credentials.Remove(Key(provider)))
                return false;
            Save(store);
            return true;
        }
    }

    public bool Has(CloudProviderKind provider)
    {
        lock (_gate)
        {
            var store = Load();
            return store.Credentials.ContainsKey(Key(provider));
        }
    }

    private CredentialFile Load()
    {
        var path = StorePath();
        if (!File.Exists(path))
            return new CredentialFile();

        try
        {
            var blob = File.ReadAllBytes(path);
            var plain = UnprotectBlob(blob);
            if (plain is null || plain.Length == 0)
                return new CredentialFile();
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<CredentialFile>(json, JsonOpts) ?? new CredentialFile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cloud-backup credentials store");
            return new CredentialFile();
        }
    }

    private void Save(CredentialFile store)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(store, JsonOpts);
        var plain = Encoding.UTF8.GetBytes(json);
        var protectedBlob = ProtectBlob(plain);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(tmp, protectedBlob);
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "secrets", "cloud-backup.json");

    private static string Key(CloudProviderKind provider) => provider.ToString();

    private static CloudCredentialEnvelope Clone(CloudCredentialEnvelope e) =>
        new()
        {
            Provider = e.Provider,
            AccountId = e.AccountId,
            AccountDisplayName = e.AccountDisplayName,
            AccountEmail = e.AccountEmail,
            ProtectedPayload = e.ProtectedPayload,
            UpdatedUtc = e.UpdatedUtc
        };

    private byte[] ProtectBlob(byte[] plain)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex) when (AllowPlaintextFallback())
            {
                _logger.LogWarning(ex, "DPAPI Protect failed; using plaintext fallback for tests");
                return EncodePlainFallback(plain);
            }
        }

        if (AllowPlaintextFallback())
            return EncodePlainFallback(plain);

        throw new InvalidOperationException(
            "Cloud credential protection requires Windows DPAPI (or JOTDEX_CLOUD_CRED_PLAINTEXT=1 for tests).");
    }

    private byte[]? UnprotectBlob(byte[] blob)
    {
        if (blob.Length >= 8 && Encoding.ASCII.GetString(blob, 0, 8) == "JDXCPLN1")
            return blob.AsSpan(8).ToArray();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                return ProtectedData.Unprotect(blob, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            catch (Exception ex) when (AllowPlaintextFallback())
            {
                _logger.LogWarning(ex, "DPAPI Unprotect failed; treating as base64 plaintext fallback");
                try { return Convert.FromBase64String(Encoding.UTF8.GetString(blob)); }
                catch { return null; }
            }
        }

        if (AllowPlaintextFallback())
        {
            try { return Convert.FromBase64String(Encoding.UTF8.GetString(blob)); }
            catch { return blob; }
        }

        return null;
    }

    private static bool AllowPlaintextFallback() =>
        !OperatingSystem.IsWindows()
        || string.Equals(Environment.GetEnvironmentVariable("JOTDEX_CLOUD_CRED_PLAINTEXT"), "1", StringComparison.Ordinal);

    private static byte[] EncodePlainFallback(byte[] plain)
    {
        // Marker + raw UTF-8 bytes so tests can round-trip without DPAPI.
        var marker = Encoding.ASCII.GetBytes("JDXCPLN1");
        var result = new byte[marker.Length + plain.Length];
        Buffer.BlockCopy(marker, 0, result, 0, marker.Length);
        Buffer.BlockCopy(plain, 0, result, marker.Length, plain.Length);
        return result;
    }

    private sealed class CredentialFile
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, CloudCredentialEnvelope> Credentials { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
