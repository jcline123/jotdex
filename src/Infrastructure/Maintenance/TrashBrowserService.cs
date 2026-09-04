using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class TrashItemDto
{
    public required string Id { get; init; }
    public required string Kind { get; init; } // note | folder
    public required string OriginalRelativePath { get; init; }
    public required string Title { get; init; }
    public required string DeletedDay { get; init; }
    public DateTimeOffset DeletedAtUtc { get; init; }
    public long Bytes { get; init; }
    public bool HasAssets { get; init; }
}

public sealed class TrashActionResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? RestoredRelativePath { get; init; }
}

public interface ITrashBrowserService
{
    IReadOnlyList<TrashItemDto> List();
    TrashActionResult Restore(string trashItemId, bool asCopy);
    TrashActionResult DeletePermanent(string trashItemId);
}

public sealed class TrashBrowserService : ITrashBrowserService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IVaultPathGuard _paths;
    private readonly IVaultService _vault;
    private readonly ILogger<TrashBrowserService> _logger;

    public TrashBrowserService(
        IDataRootResolver dataRoot,
        IVaultPathGuard paths,
        IVaultService vault,
        ILogger<TrashBrowserService> logger)
    {
        _dataRoot = dataRoot;
        _paths = paths;
        _vault = vault;
        _logger = logger;
    }

    private string TrashRoot => Path.Combine(_dataRoot.ResolveDataRoot(), "trash");

    public IReadOnlyList<TrashItemDto> List()
    {
        var root = TrashRoot;
        if (!Directory.Exists(root)) return Array.Empty<TrashItemDto>();

        var items = new List<TrashItemDto>();
        foreach (var md in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var relFromTrash = Path.GetRelativePath(root, md).Replace('\\', '/');
                var parts = relFromTrash.Split('/', 2);
                if (parts.Length < 2) continue;
                var day = parts[0];
                var original = parts[1];
                var assets = Path.ChangeExtension(md, null) + ".assets";
                var fi = new FileInfo(md);
                long bytes = fi.Length;
                var hasAssets = Directory.Exists(assets);
                if (hasAssets)
                {
                    try
                    {
                        bytes += Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
                    }
                    catch { /* ignore */ }
                }

                var deletedAt = fi.LastWriteTimeUtc;
                if (DateTime.TryParseExact(day, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var dayDt))
                    deletedAt = DateTime.SpecifyKind(dayDt, DateTimeKind.Utc);

                items.Add(new TrashItemDto
                {
                    Id = relFromTrash,
                    Kind = "note",
                    OriginalRelativePath = original,
                    Title = Path.GetFileNameWithoutExtension(original.Replace('/', Path.DirectorySeparatorChar)),
                    DeletedDay = day,
                    DeletedAtUtc = deletedAt,
                    Bytes = bytes,
                    HasAssets = hasAssets
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skip trash entry {Path}", md);
            }
        }

        return items
            .OrderByDescending(i => i.DeletedAtUtc)
            .ThenBy(i => i.OriginalRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TrashActionResult Restore(string trashItemId, bool asCopy)
    {
        if (!_paths.IsConfigured)
            return new TrashActionResult { Success = false, Error = "Vault not configured" };

        try
        {
            var (srcMd, originalRel) = ResolveTrashNote(trashItemId);
            var destRel = asCopy
                ? UniqueRestoredRelative(originalRel, forceSuffix: true)
                : File.Exists(VaultAbs(originalRel))
                    ? UniqueRestoredRelative(originalRel, forceSuffix: true)
                    : originalRel.Replace('\\', '/');

            var destAbs = VaultAbs(destRel);
            Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
            _paths.EnsureInsideVault(destAbs);

            File.Move(srcMd, destAbs);

            var srcAssets = Path.ChangeExtension(srcMd, null) + ".assets";
            if (Directory.Exists(srcAssets))
            {
                var destAssets = Path.ChangeExtension(destAbs, null) + ".assets";
                if (Directory.Exists(destAssets))
                    destAssets = destAssets + "." + Guid.NewGuid().ToString("N")[..6];
                Directory.Move(srcAssets, destAssets);
            }

            NoteFoldSidecar.MoveBeside(srcMd, destAbs);

            PruneEmptyTrashParents(Path.GetDirectoryName(srcMd)!);
            _vault.Rescan();
            return new TrashActionResult { Success = true, RestoredRelativePath = destRel.Replace('\\', '/') };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trash restore failed for {Id}", trashItemId);
            return new TrashActionResult { Success = false, Error = ex.Message };
        }
    }

    public TrashActionResult DeletePermanent(string trashItemId)
    {
        try
        {
            var (srcMd, _) = ResolveTrashNote(trashItemId);
            var srcAssets = Path.ChangeExtension(srcMd, null) + ".assets";
            File.Delete(srcMd);
            if (Directory.Exists(srcAssets))
                Directory.Delete(srcAssets, recursive: true);
            NoteFoldSidecar.DeleteBeside(srcMd);
            PruneEmptyTrashParents(Path.GetDirectoryName(srcMd)!);
            return new TrashActionResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permanent trash delete failed for {Id}", trashItemId);
            return new TrashActionResult { Success = false, Error = ex.Message };
        }
    }

    private (string AbsoluteMd, string OriginalRelative) ResolveTrashNote(string trashItemId)
    {
        var id = (trashItemId ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(id) || id.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid trash item id.");

        var abs = Path.GetFullPath(Path.Combine(TrashRoot, id.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(TrashRoot);
        if (!abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trash path escape blocked.");
        if (!File.Exists(abs) || !abs.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Trash note not found.", abs);

        var relFromTrash = Path.GetRelativePath(rootFull, abs).Replace('\\', '/');
        var slash = relFromTrash.IndexOf('/');
        if (slash < 0) throw new InvalidOperationException("Malformed trash layout.");
        return (abs, relFromTrash[(slash + 1)..]);
    }

    private string VaultAbs(string relative) =>
        _paths.EnsureInsideVault(relative.Replace('/', Path.DirectorySeparatorChar));

    private string UniqueRestoredRelative(string originalRel, bool forceSuffix)
    {
        var norm = originalRel.Replace('\\', '/');
        var dir = Path.GetDirectoryName(norm.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "";
        var stem = Path.GetFileNameWithoutExtension(norm.Replace('/', Path.DirectorySeparatorChar));
        var ext = Path.GetExtension(norm.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(ext)) ext = ".md";

        string Candidate(int n)
        {
            var suffix = n <= 1 ? " (restored)" : $" (restored {n})";
            var name = stem + suffix + ext;
            return string.IsNullOrEmpty(dir) ? name : dir.TrimEnd('/') + "/" + name;
        }

        if (!forceSuffix && !File.Exists(VaultAbs(norm)))
            return norm;

        for (var n = 1; n < 1000; n++)
        {
            var c = Candidate(n);
            if (!File.Exists(VaultAbs(c))) return c;
        }

        return Candidate(Environment.TickCount & 0xFFFF);
    }

    private void PruneEmptyTrashParents(string startDir)
    {
        var root = Path.GetFullPath(TrashRoot);
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            var full = Path.GetFullPath(dir);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                break;
            if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any())
            {
                Directory.Delete(full, recursive: false);
                dir = Path.GetDirectoryName(full);
                continue;
            }
            break;
        }
    }
}
