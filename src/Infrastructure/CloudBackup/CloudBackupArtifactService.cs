using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Maintenance;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupArtifactService : ICloudBackupArtifactService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ICloudBackupSnapshotService _snapshot;
    private readonly IMoveKitService _moveKit;
    private readonly IMoveKitCryptoService _crypto;
    private readonly IVaultSnapshotZipService _vaultZip;
    private readonly CloudBackupHashService _hashes;
    private readonly ICloudBackupSettingsService _settings;
    private readonly IAppVersion _version;
    private readonly ILogger<CloudBackupArtifactService> _logger;

    public CloudBackupArtifactService(
        IDataRootResolver dataRoot,
        ICloudBackupSnapshotService snapshot,
        IMoveKitService moveKit,
        IMoveKitCryptoService crypto,
        IVaultSnapshotZipService vaultZip,
        CloudBackupHashService hashes,
        ICloudBackupSettingsService settings,
        IAppVersion version,
        ILogger<CloudBackupArtifactService> logger)
    {
        _dataRoot = dataRoot;
        _snapshot = snapshot;
        _moveKit = moveKit;
        _crypto = crypto;
        _vaultZip = vaultZip;
        _hashes = hashes;
        _settings = settings;
        _version = version;
        _logger = logger;
    }

    public async Task<CloudBackupArtifactResult> CreateAsync(
        string runId,
        bool includePlainVaultZip,
        string? passwordForInit,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Get();
        if (!_crypto.IsPasswordProtectionEnabled)
            return Fail("Cloud backup requires a Jotdex unlock password (encrypted Move Kit).", CloudBackupFailureCode.EncryptionRequired);

        if (!_crypto.HasEncryptionKey)
        {
            if (string.IsNullOrEmpty(passwordForInit))
                return Fail("Move Kit encryption is not initialized. Re-enter your password.", CloudBackupFailureCode.EncryptionRequired);
            try { _crypto.EnsureInitialized(passwordForInit); }
            catch (Exception ex)
            {
                return Fail("Could not initialize encryption: " + ex.Message, CloudBackupFailureCode.EncryptionRequired);
            }
        }

        var snap = await _snapshot.CreateAsync(runId, cancellationToken).ConfigureAwait(false);
        if (!snap.Success || string.IsNullOrWhiteSpace(snap.VaultSnapshotPath) || string.IsNullOrWhiteSpace(snap.StagingRoot))
            return Fail(snap.Error ?? "Snapshot failed.", CloudBackupFailureCode.SnapshotFailed);

        var artifactsDir = CloudBackupPaths.ArtifactsDirectory(_dataRoot, runId);
        Directory.CreateDirectory(artifactsDir);
        var createdUtc = DateTimeOffset.UtcNow;
        var moveName = CloudBackupRemoteNaming.MoveKitFileName(createdUtc, runId);
        var stem = Path.GetFileNameWithoutExtension(moveName);

        try
        {
            var kit = await _moveKit.CreateFromVaultSnapshotAsync(
                snap.VaultSnapshotPath,
                includeAuth: true,
                includeHistory: true,
                passwordForInit: passwordForInit,
                outputDirectory: artifactsDir,
                fileNameStem: stem,
                ct: cancellationToken).ConfigureAwait(false);

            if (!kit.Success || string.IsNullOrWhiteSpace(kit.BundlePath))
                return Fail(kit.Error ?? "Move Kit creation failed.", CloudBackupFailureCode.LocalArtifactCreationFailed);

            if (!kit.Encrypted || !kit.BundlePath.EndsWith(".jotdexkit", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(kit.BundlePath); } catch { /* ignore */ }
                return Fail("Cloud backup requires an encrypted .jotdexkit.", CloudBackupFailureCode.EncryptionRequired);
            }

            // Ensure final name matches remote naming (CreateFromVaultSnapshot may have used stem.zip → .jotdexkit)
            var movePath = Path.Combine(artifactsDir, moveName);
            if (!string.Equals(Path.GetFullPath(kit.BundlePath), Path.GetFullPath(movePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Move(kit.BundlePath, movePath, overwrite: true);
            }

            var moveSha = _hashes.Sha256FileHex(movePath);
            var moveSize = new FileInfo(movePath).Length;
            var artifacts = new List<CloudBackupArtifactDescriptor>
            {
                new()
                {
                    Type = CloudArtifactTypes.MoveKit,
                    FileName = moveName,
                    LocalPath = movePath,
                    Encrypted = true,
                    KitFormat = MoveKitCryptoService.MagicV2,
                    SizeBytes = moveSize,
                    Sha256 = moveSha
                }
            };

            var required = new List<string> { CloudArtifactTypes.MoveKit };

            if (includePlainVaultZip)
            {
                var zipName = CloudBackupRemoteNaming.VaultZipFileName(createdUtc, runId);
                var zip = await _vaultZip.CreateAsync(
                    snap.VaultSnapshotPath,
                    snap.StagingRoot,
                    artifactsDir,
                    runId,
                    settings.BackupSetId,
                    zipName,
                    cancellationToken).ConfigureAwait(false);

                if (!zip.Success || string.IsNullOrWhiteSpace(zip.ZipPath) || string.IsNullOrWhiteSpace(zip.Sha256))
                    return Fail(zip.Error ?? "Vault ZIP failed.", CloudBackupFailureCode.VaultZipValidationFailed);

                artifacts.Add(new CloudBackupArtifactDescriptor
                {
                    Type = CloudArtifactTypes.VaultZip,
                    FileName = zipName,
                    LocalPath = zip.ZipPath,
                    Encrypted = false,
                    SizeBytes = zip.SizeBytes,
                    Sha256 = zip.Sha256
                });
                required.Add(CloudArtifactTypes.VaultZip);
            }

            var manifestName = CloudBackupRemoteNaming.GenerationManifestFileName(createdUtc, runId);
            var manifestPath = Path.Combine(artifactsDir, manifestName);
            var generationDoc = new
            {
                schemaVersion = 1,
                kind = "jotdex-cloud-backup-generation",
                backupSetId = settings.BackupSetId,
                runId,
                createdUtc,
                jotdexVersion = _version.Version,
                requiredArtifacts = required,
                artifacts = artifacts.Select(a => new
                {
                    type = a.Type,
                    fileName = a.FileName,
                    encrypted = a.Encrypted,
                    kitFormat = a.KitFormat,
                    sizeBytes = a.SizeBytes,
                    sha256 = a.Sha256
                })
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(generationDoc, JsonOpts), cancellationToken)
                .ConfigureAwait(false);

            var generation = new CloudBackupGeneration
            {
                BackupSetId = settings.BackupSetId,
                RunId = runId,
                CreatedUtc = createdUtc,
                JotdexVersion = _version.Version,
                RequiredArtifacts = required,
                Artifacts = artifacts,
                ManifestPath = manifestPath,
                ManifestFileName = manifestName,
                StagingRoot = snap.StagingRoot
            };

            return new CloudBackupArtifactResult { Success = true, Generation = generation };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud backup artifact creation failed");
            _snapshot.DeleteRunStaging(runId);
            return Fail(ex.Message, CloudBackupFailureCode.LocalArtifactCreationFailed);
        }
    }

    private static CloudBackupArtifactResult Fail(string error, CloudBackupFailureCode _) =>
        new() { Success = false, Error = error };
}
