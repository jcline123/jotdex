using System.Text.Json.Serialization;

namespace Jotdex.Core.CloudBackup;

public sealed class CloudBackupSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string BackupSetId { get; set; } = "";
    public string BackupSetName { get; set; } = "";
    public int IntervalHours { get; set; } = 24;
    public int VersionsToKeep { get; set; } = 3;
    public int FullVerificationIntervalDays { get; set; } = 30;
    /// <summary>When true, each generation also uploads an unencrypted vault-only ZIP.</summary>
    public bool IncludePlainVaultZip { get; set; }
    public List<CloudProviderSettings> Providers { get; set; } = [];
}

public sealed class CloudProviderSettings
{
    public CloudProviderKind Provider { get; set; }
    public bool Enabled { get; set; }
    public string? AccountId { get; set; }
    public string? AccountDisplayName { get; set; }
    public string? AccountEmail { get; set; }
    public string? RemoteRootId { get; set; }
    public string? RemoteRootDisplayPath { get; set; }

    /// <summary>OAuth app client id / Dropbox app key — set from Settings UI (or env override).</summary>
    [JsonPropertyName("oauthClientId")]
    public string? OAuthClientId { get; set; }
    /// <summary>Optional OAuth client secret (only if the provider console issued one).</summary>
    [JsonPropertyName("oauthClientSecret")]
    public string? OAuthClientSecret { get; set; }
    /// <summary>Optional override; defaults to loopback <c>/oauth/{provider}</c>.</summary>
    [JsonPropertyName("oauthRedirectUri")]
    public string? OAuthRedirectUri { get; set; }
}

public sealed class CloudArtifactBackupStatus
{
    public string ArtifactType { get; set; } = CloudArtifactTypes.MoveKit;
    public bool Required { get; set; }

    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastUploadUtc { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public DateTimeOffset? LastFullVerificationUtc { get; set; }

    public string? LastFileName { get; set; }
    public string? LastRemoteFileId { get; set; }
    public long? LastRemoteSizeBytes { get; set; }
    public string? LastSha256 { get; set; }

    public CloudBackupFailureCode LastFailureCode { get; set; }
    public string? LastFailureMessage { get; set; }
}

public sealed class CloudProviderBackupStatus
{
    public CloudProviderKind Provider { get; set; }
    public CloudConnectionState ConnectionState { get; set; }
    public CloudBackupHealthLevel Health { get; set; }

    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastUploadUtc { get; set; }
    public DateTimeOffset? LastVerifiedUtc { get; set; }
    public DateTimeOffset? LastFullVerificationUtc { get; set; }
    public DateTimeOffset? NextDueUtc { get; set; }

    public string? LastArtifactName { get; set; }
    public string? LastRemoteFileId { get; set; }
    public long? LastRemoteSizeBytes { get; set; }

    public CloudBackupFailureCode LastFailureCode { get; set; }
    public string? LastFailureMessage { get; set; }
    public int ConsecutiveFailures { get; set; }

    public long? QuotaTotalBytes { get; set; }
    public long? QuotaUsedBytes { get; set; }
    public long? QuotaRemainingBytes { get; set; }

    public CloudArtifactBackupStatus MoveKit { get; set; } = new()
    {
        ArtifactType = CloudArtifactTypes.MoveKit,
        Required = true
    };

    public CloudArtifactBackupStatus VaultZip { get; set; } = new()
    {
        ArtifactType = CloudArtifactTypes.VaultZip,
        Required = false
    };
}

public sealed class CloudBackupRuntimeState
{
    public int SchemaVersion { get; set; } = 1;
    public string? ActiveOperationId { get; set; }
    public DateTimeOffset? LastRunStartedUtc { get; set; }
    public DateTimeOffset? LastRunFinishedUtc { get; set; }
    public string? LastRunId { get; set; }
    public List<CloudProviderBackupStatus> Providers { get; set; } = [];
}

public sealed class CloudBackupOperation
{
    public required string OperationId { get; init; }
    public required string RunId { get; init; }
    public CloudBackupRunTrigger Trigger { get; init; }
    public CloudProviderKind? ProviderFilter { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public bool Running { get; set; }
    public string Phase { get; set; } = "starting";
    public string? Error { get; set; }
    public Dictionary<string, string> ProviderPhases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CloudBackupHealth
{
    public CloudBackupHealthLevel AggregateHealth { get; init; }
    public int EnabledProviderCount { get; init; }
    public bool Running { get; init; }
    public IReadOnlyList<CloudProviderHealthItem> Providers { get; init; } = [];
}

public sealed class CloudProviderHealthItem
{
    public CloudProviderKind Provider { get; init; }
    public CloudBackupHealthLevel Health { get; init; }
    public CloudConnectionState ConnectionState { get; init; }
    public CloudBackupFailureCode FailureCode { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? LastVerifiedUtc { get; init; }
}

public sealed class CloudBackupSummary
{
    public required CloudBackupSettings Settings { get; init; }
    public required CloudBackupHealth Health { get; init; }
    public required CloudBackupRuntimeState State { get; init; }
    public CloudBackupOperation? ActiveOperation { get; init; }
    public Dictionary<string, bool> ProviderAvailableInBuild { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool EncryptionReady { get; init; }
    public bool PasswordRequired { get; init; }
}
