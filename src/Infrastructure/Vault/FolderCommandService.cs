using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

public sealed class FolderCommandResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? Path { get; init; }
}

public interface IFolderCommandService
{
    FolderCommandResult Create(string parentRelativePath, string name);
    FolderCommandResult Rename(string relativePath, string newName);
    FolderCommandResult Move(string relativePath, string newParentRelativePath);
    FolderCommandResult Delete(string relativePath);
}

public sealed class FolderCommandService : IFolderCommandService
{
    private readonly IVaultPathGuard _paths;
    private readonly IVaultService _vault;
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<FolderCommandService> _logger;

    public FolderCommandService(
        IVaultPathGuard paths,
        IVaultService vault,
        IDataRootResolver dataRoot,
        ILogger<FolderCommandService> logger)
    {
        _paths = paths;
        _vault = vault;
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public FolderCommandResult Create(string parentRelativePath, string name)
    {
        if (!_paths.IsConfigured) return Fail("Vault not configured");
        var safe = SanitizeFolderName(name);
        if (safe is null) return Fail("Invalid folder name");

        try
        {
            var parent = Normalize(parentRelativePath);
            var parentAbs = string.IsNullOrEmpty(parent)
                ? _paths.VaultRoot
                : _paths.EnsureInsideVault(parent.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(parentAbs);

            var dest = Path.Combine(parentAbs, safe);
            _paths.EnsureInsideVault(dest);
            if (Directory.Exists(dest)) return Fail("Folder already exists");
            Directory.CreateDirectory(dest);

            var rel = Normalize(_paths.ToRelativePath(dest));
            _vault.Rescan();
            return new FolderCommandResult { Success = true, Path = rel };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder create failed");
            return Fail(ex.Message);
        }
    }

    public FolderCommandResult Rename(string relativePath, string newName)
    {
        if (!_paths.IsConfigured) return Fail("Vault not configured");
        var safe = SanitizeFolderName(newName);
        if (safe is null) return Fail("Invalid folder name");

        try
        {
            var oldRel = Normalize(relativePath);
            if (string.IsNullOrEmpty(oldRel)) return Fail("Cannot rename vault root");

            var abs = _paths.EnsureInsideVault(oldRel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(abs)) return Fail("Folder not found");

            var parent = Path.GetDirectoryName(abs)!;
            var dest = Path.Combine(parent, safe);
            _paths.EnsureInsideVault(dest);
            if (Directory.Exists(dest)) return Fail("A folder with that name already exists");

            Directory.Move(abs, dest);
            var newRel = Normalize(_paths.ToRelativePath(dest));
            RewriteNotesUnderVault(oldRel, newRel);
            _vault.Rescan();
            return new FolderCommandResult { Success = true, Path = newRel };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder rename failed");
            return Fail(ex.Message);
        }
    }

    public FolderCommandResult Move(string relativePath, string newParentRelativePath)
    {
        if (!_paths.IsConfigured) return Fail("Vault not configured");

        try
        {
            var oldRel = Normalize(relativePath);
            if (string.IsNullOrEmpty(oldRel)) return Fail("Cannot move vault root");

            var abs = _paths.EnsureInsideVault(oldRel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(abs)) return Fail("Folder not found");

            var name = Path.GetFileName(abs);
            var newParent = Normalize(newParentRelativePath);
            if (string.Equals(newParent, oldRel, StringComparison.OrdinalIgnoreCase) ||
                newParent.StartsWith(oldRel + "/", StringComparison.OrdinalIgnoreCase))
                return Fail("Cannot move a folder into itself");

            var parentAbs = string.IsNullOrEmpty(newParent)
                ? _paths.VaultRoot
                : _paths.EnsureInsideVault(newParent.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(parentAbs);

            var dest = Path.Combine(parentAbs, name);
            _paths.EnsureInsideVault(dest);
            if (Directory.Exists(dest)) return Fail("Destination already exists");

            Directory.Move(abs, dest);
            var newRel = Normalize(_paths.ToRelativePath(dest));
            RewriteNotesUnderVault(oldRel, newRel);
            _vault.Rescan();
            return new FolderCommandResult { Success = true, Path = newRel };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder move failed");
            return Fail(ex.Message);
        }
    }

    public FolderCommandResult Delete(string relativePath)
    {
        if (!_paths.IsConfigured) return Fail("Vault not configured");

        try
        {
            var rel = Normalize(relativePath);
            if (string.IsNullOrEmpty(rel)) return Fail("Cannot delete vault root");

            var abs = _paths.EnsureInsideVault(rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(abs)) return Fail("Folder not found");

            var trashRoot = Path.Combine(_dataRoot.ResolveDataRoot(), "trash", DateTime.UtcNow.ToString("yyyyMMdd"));
            var dest = Path.Combine(trashRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (Directory.Exists(dest)) dest += "-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(abs, dest);

            _vault.Rescan();
            return new FolderCommandResult { Success = true, Path = rel };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder delete failed");
            return Fail(ex.Message);
        }
    }

    private void RewriteNotesUnderVault(string oldFolderRel, string newFolderRel)
    {
        // Best-effort: rewrite relative folder prefixes inside all vault markdown files
        foreach (var md in Directory.EnumerateFiles(_paths.VaultRoot, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var full = _paths.EnsureInsideVault(md);
                var text = File.ReadAllText(full);
                var updated = MarkdownLinkRewriter.RewriteFolderPrefix(text, oldFolderRel, newFolderRel);
                if (!string.Equals(text, updated, StringComparison.Ordinal))
                    NoteCommandService.AtomicWrite(full, updated);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Link rewrite skipped for a note during folder change");
            }
        }
    }

    private static FolderCommandResult Fail(string error) => new() { Success = false, Error = error };

    private static string Normalize(string path) => (path ?? "").Replace('\\', '/').Trim('/');

    private static string? SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim().TrimEnd('.', ' ');
        if (name is "." or "..") return null;
        if (name.Contains('/') || name.Contains('\\')) return null;
        var invalid = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalid) >= 0) return null;
        if (name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase)) return null;
        return name;
    }
}
