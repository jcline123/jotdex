namespace Jotdex.Core.Integrations;

public interface IIntegrationConfigService
{
    IntegrationConfig Get();
    void Save(IntegrationConfig config);
    bool IsFeatureEnabled();
}

public interface IIntegrationTokenStore
{
    IReadOnlyList<IntegrationTokenPublic> List();
    IntegrationTokenRecord? Get(string id);
    IntegrationTokenCreateResult Create(IntegrationTokenCreateRequest request, string vaultId, TimeProvider time);
    bool Revoke(string id, TimeProvider time);
    int RevokeAll(TimeProvider time);
    IntegrationTokenCreateResult Rotate(string id, IntegrationTokenCreateRequest request, string vaultId, TimeProvider time);
    IntegrationTokenRecord? Authenticate(string plaintextSecret, string currentVaultId, TimeProvider time);
    void TouchLastUsed(string id, TimeProvider time);
}

public interface IIntegrationFolderAccess
{
    bool AllowsFolder(IntegrationTokenRecord token, string? folderRelativePath);
    bool AllowsNotePath(IntegrationTokenRecord token, string noteRelativePath);
    IReadOnlyList<string> FilterFolders(IntegrationTokenRecord token, IEnumerable<string> folders);
}

public interface IIntegrationActivityLog
{
    void Record(IntegrationActivityEntry entry);
    IReadOnlyList<IntegrationActivityEntry> Recent(int limit = 50);
}

public interface IIdempotencyStore
{
    IdempotencyLookupResult TryGet(string scopeKey, string key, string fingerprint);
    void Put(string scopeKey, string key, string fingerprint, int statusCode, string responseJson, TimeProvider time);
    void Prune(TimeProvider time);
}

public sealed class IdempotencyLookupResult
{
    public bool Found { get; init; }
    public bool FingerprintMismatch { get; init; }
    public int StatusCode { get; init; }
    public string? ResponseJson { get; init; }
}

public interface IVaultWriteCoordinator
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);
    T Execute<T>(Func<T> action);
}

public interface IVaultIdentity
{
    string? GetVaultId();
}
