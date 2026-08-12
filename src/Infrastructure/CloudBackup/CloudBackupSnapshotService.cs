using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupSnapshotService : ICloudBackupSnapshotService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<CloudBackupSnapshotService> _logger;

    public CloudBackupSnapshotService(
        IDataRootResolver dataRoot,
        IVaultPathGuard paths,
        ILogger<CloudBackupSnapshotService> logger)
    {
        _dataRoot = dataRoot;
        _paths = paths;
        _logger = logger;
    }

    public async Task<CloudBackupSnapshotResult> CreateAsync(string runId, CancellationToken cancellationToken)
    {
        if (!_paths.IsConfigured)
            return Fail("Vault not configured.");

        var vaultRoot = _paths.VaultRoot;
        var stagingVault = CloudBackupPaths.SnapshotVault(_dataRoot, runId);
        var stagingRun = CloudBackupPaths.RunRoot(_dataRoot, runId);

        try
        {
            if (Directory.Exists(stagingRun))
                Directory.Delete(stagingRun, recursive: true);
            Directory.CreateDirectory(stagingVault);

            var fileCount = 0;
            var noteCount = 0;
            long bytes = 0;

            await Task.Run(() =>
            {
                foreach (var file in Directory.EnumerateFiles(vaultRoot, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CopyFileContained(vaultRoot, file, stagingVault, ref fileCount, ref noteCount, ref bytes, cancellationToken);
                }
            }, cancellationToken).ConfigureAwait(false);

            return new CloudBackupSnapshotResult
            {
                Success = true,
                RunId = runId,
                StagingRoot = stagingRun,
                VaultSnapshotPath = stagingVault,
                FileCount = fileCount,
                NoteCount = noteCount,
                BytesCopied = bytes
            };
        }
        catch (OperationCanceledException)
        {
            TryDelete(stagingRun);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud backup snapshot failed for run {RunId}", runId);
            TryDelete(stagingRun);
            return Fail(ex.Message, runId);
        }
    }

    public void CleanOrphanedStaging(TimeSpan olderThan)
    {
        var root = CloudBackupPaths.StagingRoot(_dataRoot);
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.UtcNow - olderThan;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                if (info.LastWriteTimeUtc < cutoff)
                {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Removed orphaned cloud-backup staging {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clean staging {Dir}", dir);
            }
        }
    }

    public void DeleteRunStaging(string runId)
    {
        var dir = CloudBackupPaths.RunRoot(_dataRoot, runId);
        TryDelete(dir);
    }

    private void CopyFileContained(
        string vaultRoot,
        string sourceFile,
        string stagingVault,
        ref int fileCount,
        ref int noteCount,
        ref long bytes,
        CancellationToken ct)
    {
        var fullSource = Path.GetFullPath(sourceFile);
        EnsureUnder(vaultRoot, fullSource);

        if (IsReparsePoint(fullSource))
            throw new InvalidOperationException("Refusing to copy reparse point: " + Rel(vaultRoot, fullSource));

        // Also reject if any parent under vault is a reparse that escapes (best-effort).
        var rel = Rel(vaultRoot, fullSource);
        if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
            return;

        var dest = Path.GetFullPath(Path.Combine(stagingVault, rel.Replace('/', Path.DirectorySeparatorChar)));
        EnsureUnder(stagingVault, dest);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var before = new FileInfo(fullSource);
                File.Copy(fullSource, dest, overwrite: true);
                var after = new FileInfo(fullSource);
                if (before.Length != after.Length ||
                    before.LastWriteTimeUtc != after.LastWriteTimeUtc)
                {
                    last = new IOException($"File changed while copying: {rel}");
                    if (attempt < 3) continue;
                    throw last;
                }

                var copied = new FileInfo(dest);
                if (copied.Length != after.Length)
                {
                    last = new IOException($"Copy size mismatch: {rel}");
                    if (attempt < 3) continue;
                    throw last;
                }

                fileCount++;
                bytes += copied.Length;
                if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    noteCount++;
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is not OperationCanceledException)
            {
                last = ex;
                Thread.Sleep(50 * attempt);
            }
        }

        throw last ?? new IOException("Copy failed: " + rel);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return attr.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureUnder(string root, string candidate)
    {
        var r = TrimSlash(Path.GetFullPath(root));
        var c = Path.GetFullPath(candidate);
        if (!c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(c, r, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes containment root.");
    }

    private static string Rel(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    private static string TrimSlash(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* ignore */ }
    }

    private static CloudBackupSnapshotResult Fail(string error, string? runId = null) =>
        new() { Success = false, Error = error, RunId = runId };
}
