namespace Jotdex.Core.CloudBackup;

public sealed class CloudProviderAccount
{
    public required string AccountId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
}

public sealed class CloudProviderQuota
{
    public long? TotalBytes { get; init; }
    public long? UsedBytes { get; init; }
    public long? RemainingBytes { get; init; }
}

public sealed class CloudRemoteRoot
{
    public required string RootId { get; init; }
    public string? DisplayPath { get; init; }
}

public sealed class CloudRemoteFileReference
{
    public required string FileId { get; init; }
    public string? PathOrName { get; init; }
}

public sealed class CloudRemoteFile
{
    public required string FileId { get; init; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public string? ContentHash { get; init; }
    public string? Md5Checksum { get; init; }
    public string? WebUrl { get; init; }
    public DateTimeOffset? ModifiedUtc { get; init; }
}

public sealed class CloudUploadProgress
{
    public long BytesSent { get; init; }
    public long TotalBytes { get; init; }
}

public sealed class CloudUploadRequest
{
    public required CloudRemoteRoot Root { get; init; }
    public required string FileName { get; init; }
    public required string LocalPath { get; init; }
    public required string Sha256 { get; init; }
    public string? ArtifactType { get; init; }
    public string? RunId { get; init; }
    public string? BackupSetId { get; init; }
}

public sealed class CloudBackupContext
{
    public required string BackupSetId { get; init; }
    public string? BackupSetName { get; init; }
    public string? ExistingRemoteRootId { get; init; }
}

public interface ICloudBackupProvider
{
    CloudProviderKind Kind { get; }
    bool IsConfiguredInBuild { get; }

    Task<CloudProviderAccount> GetAccountAsync(CancellationToken cancellationToken);
    Task<CloudProviderQuota> GetQuotaAsync(CancellationToken cancellationToken);
    Task<CloudRemoteRoot> EnsureBackupRootAsync(CloudBackupContext context, CancellationToken cancellationToken);
    Task<CloudRemoteFile> UploadAsync(
        CloudUploadRequest request,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken);
    Task<CloudRemoteFile?> GetMetadataAsync(CloudRemoteFileReference file, CancellationToken cancellationToken);
    Task<Stream> OpenDownloadAsync(CloudRemoteFileReference file, CancellationToken cancellationToken);
    Task<IReadOnlyList<CloudRemoteFile>> ListBackupsAsync(CloudRemoteRoot root, CancellationToken cancellationToken);
    Task DeleteAsync(CloudRemoteFileReference file, CancellationToken cancellationToken);
}

public interface ICloudBackupSettingsService
{
    CloudBackupSettings Get();
    CloudBackupSettings Save(CloudBackupSettings incoming);
}

public interface ICloudBackupStateStore
{
    CloudBackupRuntimeState Get();
    void Save(CloudBackupRuntimeState state);
}

public sealed class CloudCredentialEnvelope
{
    public CloudProviderKind Provider { get; set; }
    public string? AccountId { get; set; }
    public string? AccountDisplayName { get; set; }
    public string? AccountEmail { get; set; }
    /// <summary>Opaque provider-specific refresh/cache blob (never logged).</summary>
    public string ProtectedPayload { get; set; } = "";
    public DateTimeOffset UpdatedUtc { get; set; }
}

public interface ICloudCredentialStore
{
    bool TryGet(CloudProviderKind provider, out CloudCredentialEnvelope? credential);
    void Set(CloudProviderKind provider, CloudCredentialEnvelope credential);
    bool Remove(CloudProviderKind provider);
    bool Has(CloudProviderKind provider);
}

public sealed class CloudBackupSnapshotResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? RunId { get; init; }
    public string? StagingRoot { get; init; }
    public string? VaultSnapshotPath { get; init; }
    public int FileCount { get; init; }
    public int NoteCount { get; init; }
    public long BytesCopied { get; init; }
}

public interface ICloudBackupSnapshotService
{
    Task<CloudBackupSnapshotResult> CreateAsync(string runId, CancellationToken cancellationToken);
    void CleanOrphanedStaging(TimeSpan olderThan);
    void DeleteRunStaging(string runId);
}

public sealed class VaultSnapshotZipResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? ZipPath { get; init; }
    public long SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public int EntryCount { get; init; }
    public int NoteCount { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public interface IVaultSnapshotZipService
{
    Task<VaultSnapshotZipResult> CreateAsync(
        string vaultSnapshotPath,
        string stagingRoot,
        string outputDirectory,
        string runId,
        string backupSetId,
        string fileName,
        CancellationToken cancellationToken);
}

public sealed class CloudBackupArtifactDescriptor
{
    public required string Type { get; init; }
    public required string FileName { get; init; }
    public required string LocalPath { get; init; }
    public required bool Encrypted { get; init; }
    public string? KitFormat { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed class CloudBackupGeneration
{
    public required string BackupSetId { get; init; }
    public required string RunId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string JotdexVersion { get; init; }
    public required IReadOnlyList<string> RequiredArtifacts { get; init; }
    public required IReadOnlyList<CloudBackupArtifactDescriptor> Artifacts { get; init; }
    public required string ManifestPath { get; init; }
    public required string ManifestFileName { get; init; }
    public string? StagingRoot { get; init; }
}

public sealed class CloudBackupArtifactResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public CloudBackupGeneration? Generation { get; init; }
}

public interface ICloudBackupArtifactService
{
    Task<CloudBackupArtifactResult> CreateAsync(
        string runId,
        bool includePlainVaultZip,
        string? passwordForInit,
        CancellationToken cancellationToken);
}

public interface ICloudBackupCoordinator
{
    CloudBackupSummary GetSummary();
    CloudBackupHealth GetHealth();
    Task<CloudBackupOperation> StartRunAsync(
        CloudBackupRunTrigger trigger,
        CloudProviderKind? provider,
        CancellationToken cancellationToken);
    CloudBackupOperation? GetOperation(string operationId);
}

public interface ICloudBackupHealthService
{
    CloudBackupHealth Calculate(CloudBackupSettings settings, CloudBackupRuntimeState state, bool running);
}

public interface ICloudOAuthConnectionService
{
    Task<CloudOAuthAttempt> BeginConnectAsync(CloudProviderKind provider, CancellationToken cancellationToken);
    CloudOAuthAttempt? GetAttempt(CloudProviderKind provider, string attemptId);
    Task<CloudOAuthAttempt> CompleteAsync(CloudProviderKind provider, string attemptId, string? authorizationCode, CancellationToken cancellationToken);
    Task DisconnectAsync(CloudProviderKind provider, CancellationToken cancellationToken);
}

public sealed class CloudOAuthAttempt
{
    public required string AttemptId { get; init; }
    public CloudProviderKind Provider { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public bool Completed { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? AuthorizeUrl { get; set; }
    public string? AccountDisplayName { get; set; }
    public string? AccountEmail { get; set; }
}
