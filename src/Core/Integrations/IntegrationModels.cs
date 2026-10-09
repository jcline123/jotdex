namespace Jotdex.Core.Integrations;

public static class IntegrationScopes
{
    public const string NotesRead = "notes:read";
    public const string NotesCreate = "notes:create";
    public const string NotesAppend = "notes:append";
    public const string NotesUpdate = "notes:update";
    public const string NotesInsert = "notes:insert";
    public const string AttachmentsRead = "attachments:read";
    public const string AttachmentsWrite = "attachments:write";
    public const string TasksRead = "tasks:read";
    public const string TasksWrite = "tasks:write";

    public static readonly IReadOnlyList<string> All =
    [
        NotesRead, NotesCreate, NotesAppend, NotesUpdate, NotesInsert,
        AttachmentsRead, AttachmentsWrite, TasksRead, TasksWrite
    ];

    public static IReadOnlyList<string> PresetReadOnly => [NotesRead];
    public static IReadOnlyList<string> PresetCaptureAppend => [NotesRead, NotesCreate, NotesAppend];
    public static IReadOnlyList<string> PresetReadEdit => [NotesRead, NotesCreate, NotesAppend, NotesUpdate];
}

public enum NoteChangeVia
{
    Ui,
    Api,
    Import,
    External,
    Unknown
}

/// <summary>Immutable actor context for managed note mutations. Never from client body/headers.</summary>
public sealed record NoteChangeContext(
    NoteChangeVia Via,
    string? ActorDisplayName = null,
    string? IntegrationTokenId = null);

public sealed class IntegrationConfig
{
    public bool Enabled { get; set; }
    public int ReadRequestsPerMinute { get; set; } = 120;
    public int WriteRequestsPerMinute { get; set; } = 20;
    public int MaxBodyBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxPageSize { get; set; } = 100;
    public int IdempotencyRetentionHours { get; set; } = 24;
}

public sealed class IntegrationTokenRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SecretVerifierSha256 { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public required IReadOnlyList<string> AllowedFolderRoots { get; init; }
    public bool WholeVault { get; init; }
    public required string VaultId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed class IntegrationTokenCreateRequest
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public IReadOnlyList<string> AllowedFolderRoots { get; init; } = [];
    public bool WholeVault { get; init; }
    /// <summary>Days until expiry (1–365). Ignored when <see cref="NeverExpires"/> is true. Use 0 with NeverExpires.</summary>
    public int ExpirationDays { get; init; } = 90;
    /// <summary>When true, token does not expire until revoked (ExpiresAt stored as MaxValue).</summary>
    public bool NeverExpires { get; init; }
    public string? AdminPassword { get; init; }
}

public sealed class IntegrationTokenCreateResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? TokenId { get; init; }
    public string? PlaintextSecret { get; init; }
    public IntegrationTokenPublic? Token { get; init; }
}

public sealed class IntegrationTokenPublic
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Scopes { get; init; }
    public required IReadOnlyList<string> AllowedFolderRoots { get; init; }
    public bool WholeVault { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool NeverExpires { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool Enabled { get; init; }
    public bool Expired { get; init; }
    public bool Active { get; init; }
}

public static class IntegrationTokenExpiry
{
    /// <summary>Sentinel stored for never-expiring tokens.</summary>
    public static readonly DateTimeOffset Never = DateTimeOffset.MaxValue;

    public static bool IsNever(DateTimeOffset expiresAt) => expiresAt.Year >= 9999;
}

public sealed class IntegrationActivityEntry
{
    public required DateTimeOffset At { get; init; }
    public required string TokenId { get; init; }
    public required string TokenName { get; init; }
    public required string Operation { get; init; }
    public string? NoteId { get; init; }
    public required string Outcome { get; init; }
    public string? RequestId { get; init; }
    public string? EtagBefore { get; init; }
    public string? EtagAfter { get; init; }
}

public static class IntegrationAuth
{
    public const string Scheme = "JotdexIntegrationToken";
    public const string Policy = "IntegrationApi";
    public const string AdminPolicy = "IntegrationAdmin";
    public const string TokenPrefix = "jdx_";
    public const string ClaimTokenId = "jotdex_integration_token_id";
    public const string ClaimTokenName = "jotdex_integration_token_name";
    public const string ClaimScopes = "jotdex_integration_scopes";
    public const string ClaimWholeVault = "jotdex_integration_whole_vault";
    public const string ClaimFolders = "jotdex_integration_folders";
    public const string ClaimVaultId = "jotdex_integration_vault_id";
    public const string ClaimExpiresAt = "jotdex_integration_expires_at";
}

public static class ProvenanceKeys
{
    public const string CreatedVia = "jotdex_created_via";
    public const string CreatedBy = "jotdex_created_by";
    public const string UpdatedVia = "jotdex_updated_via";
    public const string UpdatedBy = "jotdex_updated_by";
    public const string LastApiUpdateAt = "jotdex_last_api_update_at";
    public const string LastApiUpdateBy = "jotdex_last_api_update_by";
    public const string ProvenanceHash = "jotdex_provenance_hash";

    public static readonly HashSet<string> ServerOwned = new(StringComparer.OrdinalIgnoreCase)
    {
        "modified",
        CreatedVia, CreatedBy, UpdatedVia, UpdatedBy, LastApiUpdateAt, LastApiUpdateBy, ProvenanceHash
    };
}
