using System.Collections.Concurrent;
using Jotdex.Core.CloudBackup;

namespace Jotdex.Infrastructure.CloudBackup;

/// <summary>In-memory / filesystem fake provider for unit tests. Script failures via public properties.</summary>
public sealed class FakeCloudBackupProvider : ICloudBackupProvider
{
    private readonly ConcurrentDictionary<string, CloudRemoteFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootDir;
    private CloudRemoteRoot _root = new() { RootId = "fake-root", DisplayPath = "/Jotdex/Backups" };

    public FakeCloudBackupProvider(CloudProviderKind kind = CloudProviderKind.Dropbox, string? storageDirectory = null)
    {
        Kind = kind;
        _rootDir = storageDirectory ?? Path.Combine(Path.GetTempPath(), "jotdex-fake-cloud", kind.ToString(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public CloudProviderKind Kind { get; }
    public bool IsConfiguredInBuild { get; set; } = true;

    /// <summary>
    /// When true, this fake stands in for a real provider in Development because OAuth client IDs are unset.
    /// Portable / non-Development builds never register these.
    /// </summary>
    public bool IsLocalDevelopmentFallback { get; set; }

    public bool FailEnsureRoot { get; set; }
    public bool FailUpload { get; set; }
    public bool FailList { get; set; }
    public bool FailDelete { get; set; }
    public bool FailMetadata { get; set; }
    public bool FailQuota { get; set; }
    public bool FailAccount { get; set; }
    public bool SizeMismatchOnVerify { get; set; }
    /// <summary>When set, uploads whose <see cref="CloudUploadRequest.ArtifactType"/> match fail.</summary>
    public HashSet<string> FailArtifactTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public CloudBackupFailureCode FailureCode { get; set; } = CloudBackupFailureCode.UploadFailed;
    public string? FailureMessage { get; set; }

    public string? AccountId { get; set; } = "fake-account";
    public string? AccountDisplayName { get; set; } = "Fake User";
    public string? AccountEmail { get; set; } = "fake@example.com";
    public long? QuotaTotalBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public long? QuotaUsedBytes { get; set; } = 100L * 1024 * 1024;

    public string StorageDirectory => _rootDir;

    public Task<CloudProviderAccount> GetAccountAsync(CancellationToken cancellationToken)
    {
        if (FailAccount) throw Fail();
        return Task.FromResult(new CloudProviderAccount
        {
            AccountId = AccountId ?? "fake",
            DisplayName = AccountDisplayName,
            Email = AccountEmail
        });
    }

    public Task<CloudProviderQuota> GetQuotaAsync(CancellationToken cancellationToken)
    {
        if (FailQuota) throw Fail();
        return Task.FromResult(new CloudProviderQuota
        {
            TotalBytes = QuotaTotalBytes,
            UsedBytes = QuotaUsedBytes,
            RemainingBytes = QuotaTotalBytes is long t && QuotaUsedBytes is long u ? t - u : null
        });
    }

    public Task<CloudRemoteRoot> EnsureBackupRootAsync(CloudBackupContext context, CancellationToken cancellationToken)
    {
        if (FailEnsureRoot) throw Fail();
        _root = new CloudRemoteRoot
        {
            RootId = string.IsNullOrWhiteSpace(context.ExistingRemoteRootId) ? "fake-root-" + context.BackupSetId : context.ExistingRemoteRootId!,
            DisplayPath = $"/Jotdex/Backups/{context.BackupSetId}"
        };
        Directory.CreateDirectory(Path.Combine(_rootDir, context.BackupSetId));
        return Task.FromResult(_root);
    }

    public void SeedRemoteFile(string fileName, long sizeBytes = 10)
    {
        var destPath = Path.Combine(_rootDir, fileName);
        File.WriteAllBytes(destPath, new byte[Math.Max(1, (int)Math.Min(sizeBytes, 1024))]);
        var remote = new CloudRemoteFile
        {
            FileId = "fake:" + fileName,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ModifiedUtc = DateTimeOffset.UtcNow
        };
        _files[remote.FileId] = remote;
    }

    public IReadOnlyCollection<string> RemoteFileNames =>
        _files.Values.Select(f => f.FileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<CloudRemoteFile> UploadAsync(
        CloudUploadRequest request,
        IProgress<CloudUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (FailUpload) throw Fail();
        if (!string.IsNullOrEmpty(request.ArtifactType) && FailArtifactTypes.Contains(request.ArtifactType))
            throw Fail();
        await using var src = File.OpenRead(request.LocalPath);
        var destPath = Path.Combine(_rootDir, request.FileName);
        await using (var dst = File.Create(destPath))
        {
            var buffer = new byte[64 * 1024];
            long sent = 0;
            var total = src.Length;
            int read;
            while ((read = await src.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += read;
                progress?.Report(new CloudUploadProgress { BytesSent = sent, TotalBytes = total });
            }
        }

        var info = new FileInfo(destPath);
        var remote = new CloudRemoteFile
        {
            FileId = "fake:" + request.FileName,
            FileName = request.FileName,
            SizeBytes = SizeMismatchOnVerify ? info.Length + 1 : info.Length,
            ContentHash = request.Sha256,
            ModifiedUtc = DateTimeOffset.UtcNow
        };
        _files[remote.FileId] = remote;
        return remote;
    }

    public Task<CloudRemoteFile?> GetMetadataAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        if (FailMetadata) throw Fail();
        _files.TryGetValue(file.FileId, out var remote);
        if (remote is null && !string.IsNullOrWhiteSpace(file.PathOrName))
            remote = _files.Values.FirstOrDefault(f =>
                string.Equals(f.FileName, Path.GetFileName(file.PathOrName), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(remote);
    }

    public Task<Stream> OpenDownloadAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        if (!_files.TryGetValue(file.FileId, out var remote))
            throw new FileNotFoundException("Fake remote file missing.");
        var path = Path.Combine(_rootDir, remote.FileName);
        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task<IReadOnlyList<CloudRemoteFile>> ListBackupsAsync(CloudRemoteRoot root, CancellationToken cancellationToken)
    {
        if (FailList) throw Fail();
        return Task.FromResult<IReadOnlyList<CloudRemoteFile>>(_files.Values.OrderBy(f => f.FileName).ToList());
    }

    public Task DeleteAsync(CloudRemoteFileReference file, CancellationToken cancellationToken)
    {
        if (FailDelete) throw Fail();
        if (_files.TryRemove(file.FileId, out var remote))
        {
            var path = Path.Combine(_rootDir, remote.FileName);
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
        return Task.CompletedTask;
    }

    private CloudBackupProviderException Fail() =>
        new(FailureCode, FailureMessage ?? FailureCode.ToString());
}
