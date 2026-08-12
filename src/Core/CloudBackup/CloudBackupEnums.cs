namespace Jotdex.Core.CloudBackup;

public enum CloudProviderKind
{
    OneDrive = 0,
    GoogleDrive = 1,
    Dropbox = 2
}

public enum CloudConnectionState
{
    NotConfigured = 0,
    Connecting = 1,
    Connected = 2,
    ReconnectRequired = 3,
    Disconnected = 4,
    ConfigurationUnavailable = 5
}

public enum CloudBackupHealthLevel
{
    NotConfigured = 0,
    Pending = 1,
    Healthy = 2,
    Warning = 3,
    Error = 4,
    Running = 5
}

public enum CloudBackupFailureCode
{
    None = 0,
    EncryptionRequired = 1,
    LocalArtifactCreationFailed = 2,
    ProviderConfigurationMissing = 3,
    AuthenticationRequired = 4,
    AuthorizationDenied = 5,
    TokenRefreshFailed = 6,
    QuotaExceeded = 7,
    NetworkUnavailable = 8,
    RateLimited = 9,
    ProviderUnavailable = 10,
    UploadFailed = 11,
    RemoteFileMissing = 12,
    RemoteSizeMismatch = 13,
    RemoteChecksumMismatch = 14,
    FullVerificationFailed = 15,
    RetentionFailed = 16,
    Cancelled = 17,
    Unknown = 18,
    SnapshotFailed = 19,
    VaultZipValidationFailed = 20
}

public enum CloudBackupRunTrigger
{
    Scheduled = 0,
    Manual = 1,
    ProviderConnected = 2,
    Retry = 3,
    StartupCatchUp = 4
}

public static class CloudArtifactTypes
{
    public const string MoveKit = "moveKit";
    public const string VaultZip = "vaultZip";
}
