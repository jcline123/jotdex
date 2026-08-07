using System.IO.Compression;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class BackupBundleResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? BundlePath { get; init; }
    public long Bytes { get; init; }
    public bool IncludedAuth { get; init; }
    public bool IncludedHistory { get; init; }
}

public interface IBackupBundleService
{
    Task<BackupBundleResult> CreateAsync(bool includeAuth, bool includeHistory, CancellationToken ct = default);
}

/// <summary>ZIP the live vault plus selected app-data folders (config always; auth/history optional).</summary>
public sealed class BackupBundleService : IBackupBundleService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<BackupBundleService> _logger;

    public BackupBundleService(IDataRootResolver dataRoot, IVaultPathGuard paths, ILogger<BackupBundleService> logger)
    {
        _dataRoot = dataRoot;
        _paths = paths;
        _logger = logger;
    }

    public async Task<BackupBundleResult> CreateAsync(bool includeAuth, bool includeHistory, CancellationToken ct = default)
    {
        if (!_paths.IsConfigured)
            return new BackupBundleResult { Success = false, Error = "Vault not configured." };

        var vault = _paths.VaultRoot;
        if (!Directory.Exists(vault))
            return new BackupBundleResult { Success = false, Error = "Vault folder missing." };

        var dataRoot = _dataRoot.ResolveDataRoot();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var outDir = Path.Combine(dataRoot, "exports", "backups");
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"jotdex-backup-{stamp}.zip");

        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                AddDirectory(zip, vault, "vault", ct);

                var configDir = Path.Combine(dataRoot, "config");
                if (Directory.Exists(configDir))
                    AddDirectory(zip, configDir, "appdata/config", ct);

                if (includeAuth)
                {
                    var authDir = Path.Combine(dataRoot, "auth");
                    if (Directory.Exists(authDir))
                        AddDirectory(zip, authDir, "appdata/auth", ct);
                }

                if (includeHistory)
                {
                    var histDir = Path.Combine(dataRoot, "history");
                    if (Directory.Exists(histDir))
                        AddDirectory(zip, histDir, "appdata/history", ct);
                }

                var manifest = new
                {
                    createdUtc = DateTimeOffset.UtcNow,
                    vaultPath = vault,
                    includeAuth,
                    includeHistory,
                    note = "Restore vault/ to local disk; restore appdata pieces into the Jotdex data root. Indexes are not included — rebuild via Rescan."
                };
                var entry = zip.CreateEntry("MANIFEST.json");
                using var w = new StreamWriter(entry.Open());
                w.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }, ct).ConfigureAwait(false);

            var bytes = new FileInfo(zipPath).Length;
            _logger.LogInformation("Backup bundle created: {Path} ({Bytes} bytes)", zipPath, bytes);
            return new BackupBundleResult
            {
                Success = true,
                BundlePath = zipPath,
                Bytes = bytes,
                IncludedAuth = includeAuth,
                IncludedHistory = includeHistory
            };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
            _logger.LogWarning(ex, "Backup bundle failed");
            return new BackupBundleResult { Success = false, Error = ex.Message };
        }
    }

    private static void AddDirectory(ZipArchive zip, string sourceDir, string entryPrefix, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            // Skip junk / VCS
            if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
                continue;

            zip.CreateEntryFromFile(file, $"{entryPrefix}/{rel}", CompressionLevel.Optimal);
        }
    }
}
