using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class TrashCleanupResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public int DeletedFiles { get; init; }
    public int DeletedDirs { get; init; }
}

public interface IMaintenanceService
{
    object GetDiagnostics();
    TrashCleanupResult EmptyTrash(bool olderThanDaysOnly = false, int olderThanDays = 30);
}

public sealed class MaintenanceService : IMaintenanceService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(IDataRootResolver dataRoot, ILogger<MaintenanceService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public object GetDiagnostics()
    {
        var root = _dataRoot.ResolveDataRoot();
        long DirSize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }
            catch { return 0; }
        }

        int FileCount(string path)
        {
            if (!Directory.Exists(path)) return 0;
            try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count(); }
            catch { return 0; }
        }

        return new
        {
            dataRoot = root,
            trash = new { path = Path.Combine(root, "trash"), files = FileCount(Path.Combine(root, "trash")), bytes = DirSize(Path.Combine(root, "trash")) },
            history = new { path = Path.Combine(root, "history"), files = FileCount(Path.Combine(root, "history")), bytes = DirSize(Path.Combine(root, "history")) },
            indexes = new { path = Path.Combine(root, "indexes"), files = FileCount(Path.Combine(root, "indexes")), bytes = DirSize(Path.Combine(root, "indexes")) },
            exports = new { path = Path.Combine(root, "exports"), files = FileCount(Path.Combine(root, "exports")), bytes = DirSize(Path.Combine(root, "exports")) },
            auth = new { configured = File.Exists(Path.Combine(root, "auth", "users.json")) }
        };
    }

    public TrashCleanupResult EmptyTrash(bool olderThanDaysOnly = false, int olderThanDays = 30)
    {
        var trash = Path.Combine(_dataRoot.ResolveDataRoot(), "trash");
        if (!Directory.Exists(trash))
            return new TrashCleanupResult { Success = true, DeletedFiles = 0, DeletedDirs = 0 };

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, olderThanDays));
        var deletedFiles = 0;
        var deletedDirs = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(trash, "*", SearchOption.AllDirectories).ToList())
            {
                if (olderThanDaysOnly && File.GetLastWriteTimeUtc(file) > cutoff)
                    continue;
                File.Delete(file);
                deletedFiles++;
            }

            foreach (var dir in Directory.EnumerateDirectories(trash, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length)
                         .ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir, recursive: false);
                    deletedDirs++;
                }
            }

            _logger.LogInformation("Trash cleanup removed {Files} files, {Dirs} dirs", deletedFiles, deletedDirs);
            return new TrashCleanupResult { Success = true, DeletedFiles = deletedFiles, DeletedDirs = deletedDirs };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trash cleanup failed");
            return new TrashCleanupResult { Success = false, Error = ex.Message, DeletedFiles = deletedFiles, DeletedDirs = deletedDirs };
        }
    }
}
