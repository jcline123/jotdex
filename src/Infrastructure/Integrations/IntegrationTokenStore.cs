using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Integrations;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Integrations;

public sealed class IntegrationTokenStore : IIntegrationTokenStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<IntegrationTokenStore> _logger;
    private readonly object _gate = new();
    private List<IntegrationTokenRecord>? _cache;
    private DateTimeOffset _lastTouchFlush = DateTimeOffset.MinValue;
    private readonly Dictionary<string, DateTimeOffset> _pendingTouches = new(StringComparer.Ordinal);

    public IntegrationTokenStore(IDataRootResolver dataRoot, ILogger<IntegrationTokenStore> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public IReadOnlyList<IntegrationTokenPublic> List()
    {
        lock (_gate)
        {
            var now = TimeProvider.System.GetUtcNow();
            return Load().Select(t => ToPublic(t, now)).ToList();
        }
    }

    public IntegrationTokenRecord? Get(string id)
    {
        lock (_gate)
        {
            return Load().FirstOrDefault(t => t.Id.Equals(id, StringComparison.Ordinal));
        }
    }

    public IntegrationTokenCreateResult Create(IntegrationTokenCreateRequest request, string vaultId, TimeProvider time)
    {
        lock (_gate)
        {
            var validation = ValidateCreate(request);
            if (validation is not null)
                return new IntegrationTokenCreateResult { Success = false, Error = validation };

            var now = time.GetUtcNow();
            var id = Guid.NewGuid().ToString("N")[..16];
            var secretBytes = RandomNumberGenerator.GetBytes(32);
            var secretB64 = Convert.ToBase64String(secretBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var plaintext = $"{IntegrationAuth.TokenPrefix}{id}_{secretB64}";
            var verifier = HashSecret(plaintext);

            var expiresAt = request.NeverExpires
                ? IntegrationTokenExpiry.Never
                : now.AddDays(Math.Clamp(request.ExpirationDays, 1, 365));

            var record = new IntegrationTokenRecord
            {
                Id = id,
                Name = request.Name.Trim(),
                SecretVerifierSha256 = verifier,
                Scopes = request.Scopes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                AllowedFolderRoots = request.WholeVault
                    ? []
                    : request.AllowedFolderRoots.Select(IntegrationFolderAccess.Normalize).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                WholeVault = request.WholeVault,
                VaultId = vaultId,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                Enabled = true
            };

            var list = Load().ToList();
            list.Add(record);
            Persist(list);
            return new IntegrationTokenCreateResult
            {
                Success = true,
                TokenId = id,
                PlaintextSecret = plaintext,
                Token = ToPublic(record, now)
            };
        }
    }

    public bool Revoke(string id, TimeProvider time)
    {
        lock (_gate)
        {
            var list = Load().ToList();
            var idx = list.FindIndex(t => t.Id.Equals(id, StringComparison.Ordinal));
            if (idx < 0) return false;
            var t = list[idx];
            if (t.RevokedAt is not null) return true;
            list[idx] = Clone(t, revokedAt: time.GetUtcNow(), enabled: false);
            Persist(list);
            return true;
        }
    }

    public int RevokeAll(TimeProvider time)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var list = Load().Select(t => t.RevokedAt is null ? Clone(t, revokedAt: now, enabled: false) : t).ToList();
            var n = list.Count(t => t.RevokedAt is not null);
            Persist(list);
            return n;
        }
    }

    public IntegrationTokenCreateResult Rotate(string id, IntegrationTokenCreateRequest request, string vaultId, TimeProvider time)
    {
        lock (_gate)
        {
            if (!Revoke(id, time))
                return new IntegrationTokenCreateResult { Success = false, Error = "Token not found" };
            return Create(request, vaultId, time);
        }
    }

    public IntegrationTokenRecord? Authenticate(string plaintextSecret, string currentVaultId, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(plaintextSecret) || string.IsNullOrWhiteSpace(currentVaultId))
            return null;

        lock (_gate)
        {
            var now = time.GetUtcNow();
            var incoming = HashSecretBytes(plaintextSecret);
            foreach (var t in Load())
            {
                if (!t.Enabled || t.RevokedAt is not null) continue;
                if (!IntegrationTokenExpiry.IsNever(t.ExpiresAt) && t.ExpiresAt <= now) continue;
                if (!string.Equals(t.VaultId, currentVaultId, StringComparison.Ordinal)) continue;
                byte[] stored;
                try { stored = Convert.FromHexString(t.SecretVerifierSha256); }
                catch { continue; }
                if (stored.Length != incoming.Length) continue;
                if (!CryptographicOperations.FixedTimeEquals(stored, incoming)) continue;
                return t;
            }
            return null;
        }
    }

    public void TouchLastUsed(string id, TimeProvider time)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            _pendingTouches[id] = now;
            // Coalesce disk writes: at most every 60s
            if (now - _lastTouchFlush < TimeSpan.FromSeconds(60)) return;
            FlushTouches(now);
        }
    }

    private void FlushTouches(DateTimeOffset now)
    {
        if (_pendingTouches.Count == 0) return;
        var list = Load().ToList();
        var changed = false;
        for (var i = 0; i < list.Count; i++)
        {
            if (!_pendingTouches.TryGetValue(list[i].Id, out var at)) continue;
            list[i] = Clone(list[i], lastUsedAt: at);
            changed = true;
        }
        _pendingTouches.Clear();
        _lastTouchFlush = now;
        if (changed) Persist(list);
    }

    private static string? ValidateCreate(IntegrationTokenCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            return "Name is required (max 80 characters)";
        if (request.Scopes is null || request.Scopes.Count == 0)
            return "At least one scope is required";
        foreach (var s in request.Scopes)
        {
            if (!IntegrationScopes.All.Contains(s, StringComparer.OrdinalIgnoreCase))
                return $"Unknown scope: {s}";
        }
        if (!request.WholeVault && (request.AllowedFolderRoots is null || request.AllowedFolderRoots.Count == 0))
            return "Select folders or explicitly grant whole-vault access";
        if (!request.NeverExpires && request.ExpirationDays is < 1 or > 365)
            return "Expiration must be between 1 and 365 days, or choose never expire";
        return null;
    }

    private List<IntegrationTokenRecord> Load()
    {
        if (_cache is not null) return _cache;
        var path = StorePath();
        if (!File.Exists(path))
        {
            _cache = [];
            return _cache;
        }
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<TokenFile>(json, JsonOpts);
            _cache = doc?.Tokens?.ToList() ?? [];
            return _cache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integration token store corrupt; fail closed");
            throw new InvalidOperationException("Integration token store is corrupt", ex);
        }
    }

    private void Persist(List<IntegrationTokenRecord> list)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new TokenFile { Version = 1, Tokens = list };
        var json = JsonSerializer.Serialize(file, JsonOpts);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
        _cache = list;
    }

    private string StorePath() => Path.Combine(_dataRoot.ResolveDataRoot(), "integrations", "tokens.json");

    private static string HashSecret(string plaintext) =>
        Convert.ToHexString(HashSecretBytes(plaintext)).ToLowerInvariant();

    private static byte[] HashSecretBytes(string plaintext) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));

    private static IntegrationTokenPublic ToPublic(IntegrationTokenRecord t, DateTimeOffset now)
    {
        var never = IntegrationTokenExpiry.IsNever(t.ExpiresAt);
        var expired = !never && t.ExpiresAt <= now;
        var active = t.Enabled && t.RevokedAt is null && !expired;
        return new IntegrationTokenPublic
        {
            Id = t.Id,
            Name = t.Name,
            Scopes = t.Scopes,
            AllowedFolderRoots = t.AllowedFolderRoots,
            WholeVault = t.WholeVault,
            CreatedAt = t.CreatedAt,
            ExpiresAt = t.ExpiresAt,
            NeverExpires = never,
            RevokedAt = t.RevokedAt,
            LastUsedAt = t.LastUsedAt,
            Enabled = t.Enabled,
            Expired = expired,
            Active = active
        };
    }

    private static IntegrationTokenRecord Clone(
        IntegrationTokenRecord t,
        DateTimeOffset? revokedAt = null,
        DateTimeOffset? lastUsedAt = null,
        bool? enabled = null) => new()
    {
        Id = t.Id,
        Name = t.Name,
        SecretVerifierSha256 = t.SecretVerifierSha256,
        Scopes = t.Scopes,
        AllowedFolderRoots = t.AllowedFolderRoots,
        WholeVault = t.WholeVault,
        VaultId = t.VaultId,
        CreatedAt = t.CreatedAt,
        ExpiresAt = t.ExpiresAt,
        RevokedAt = revokedAt ?? t.RevokedAt,
        LastUsedAt = lastUsedAt ?? t.LastUsedAt,
        Enabled = enabled ?? t.Enabled
    };

    private sealed class TokenFile
    {
        public int Version { get; set; } = 1;
        public List<IntegrationTokenRecord> Tokens { get; set; } = [];
    }
}
