using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.History;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jotdex.Infrastructure.Vault;

public sealed class NoteSaveResult
{
    public required bool Success { get; init; }
    public required string ETag { get; init; }
    public string? Error { get; init; }
    public bool Conflict { get; init; }
    public NoteDetail? Note { get; init; }
}

public sealed class NoteMoveResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public NoteDetail? Note { get; init; }
}

public interface INoteCommandService
{
    NoteSaveResult Save(Guid id, string markdown, string expectedETag, bool force = false);
    NoteDetail? Create(string folderRelativePath, string title, string? markdown = null);
    bool MoveToTrash(Guid id);
    NoteSaveResult RestoreHistory(Guid id, string snapshotId);
    NoteMoveResult Move(Guid id, string targetFolderRelativePath, string? newTitle = null);
    NoteDetail? Duplicate(Guid id);
    AttachmentUploadResult AddAttachment(Guid id, Stream content, string originalFileName, string? contentType);
}

public sealed class AttachmentUploadResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? FileName { get; init; }
    /// <summary>Relative Markdown link target, e.g. Note%20Name.assets/shot.png</summary>
    public string? MarkdownPath { get; init; }
    public string? AttachmentId { get; init; }
    public string? ContentType { get; init; }
    public bool IsImage { get; init; }
    public NoteDetail? Note { get; init; }
}

public sealed class NoteCommandService : INoteCommandService
{
    private readonly IVaultPathGuard _paths;
    private readonly IVaultService _vault;
    private readonly INoteHistoryService _history;
    private readonly IDataRootResolver _dataRoot;
    private readonly JotdexOptions _options;
    private readonly ILogger<NoteCommandService> _logger;

    public NoteCommandService(
        IVaultPathGuard paths,
        IVaultService vault,
        INoteHistoryService history,
        IDataRootResolver dataRoot,
        IOptions<JotdexOptions> options,
        ILogger<NoteCommandService> logger)
    {
        _paths = paths;
        _vault = vault;
        _history = history;
        _dataRoot = dataRoot;
        _options = options.Value;
        _logger = logger;
    }

    public NoteSaveResult Save(Guid id, string markdown, string expectedETag, bool force = false)
    {
        if (!_paths.IsConfigured)
            return Fail("Vault not configured");

        var existing = _vault.GetNote(id);
        if (existing is null)
            return Fail("Note not found");

        if (!force && !string.Equals(existing.ETag, expectedETag, StringComparison.OrdinalIgnoreCase))
        {
            return new NoteSaveResult
            {
                Success = false,
                Conflict = true,
                ETag = existing.ETag,
                Error = "Note changed on disk (or in another session). Reload or overwrite.",
                Note = existing
            };
        }

        // Open→close / identical buffer: do not rewrite the file or snapshot.
        if (string.Equals(existing.Markdown, markdown, StringComparison.Ordinal))
        {
            return new NoteSaveResult
            {
                Success = true,
                ETag = existing.ETag,
                Note = existing
            };
        }

        var absolute = _paths.EnsureInsideVault(existing.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        _history.SnapshotIfChanged(id, existing.Markdown);
        AtomicWrite(absolute, markdown);
        _vault.Rescan();
        var updated = _vault.GetNote(id);
        return new NoteSaveResult
        {
            Success = true,
            ETag = updated?.ETag ?? Hash(markdown),
            Note = updated
        };
    }

    public NoteDetail? Create(string folderRelativePath, string title, string? markdown = null)
    {
        if (!_paths.IsConfigured) return null;
        var folder = (folderRelativePath ?? "").Replace('\\', '/').Trim('/');
        var safeTitle = SanitizeFileName(title);
        var dir = string.IsNullOrEmpty(folder)
            ? _paths.VaultRoot
            : _paths.EnsureInsideVault(folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);

        var fileName = safeTitle + ".md";
        var path = Path.Combine(dir, fileName);
        var n = 1;
        while (File.Exists(path))
        {
            fileName = $"{safeTitle} ({n}).md";
            path = Path.Combine(dir, fileName);
            n++;
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        var body = markdown ?? $"# {title}\n\n";
        var content =
            $"---\nid: {id:D}\ntitle: {EscapeYaml(title)}\ncreated: {now}\nmodified: {now}\ntags: []\n---\n\n{body.TrimStart()}";

        AtomicWrite(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        _vault.Rescan();
        return _vault.GetNote(id);
    }

    public bool MoveToTrash(Guid id)
    {
        if (!_paths.IsConfigured) return false;
        var note = _vault.GetNote(id);
        if (note is null) return false;

        try
        {
            var absolute = _paths.EnsureInsideVault(note.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var trashRoot = Path.Combine(_dataRoot.ResolveDataRoot(), "trash");
            var destRel = note.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            var dest = Path.Combine(trashRoot, DateTime.UtcNow.ToString("yyyyMMdd"), destRel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest)) dest = dest + "." + Guid.NewGuid().ToString("N")[..8];
            File.Move(absolute, dest);

            var assets = Path.ChangeExtension(absolute, null) + ".assets";
            if (Directory.Exists(assets))
            {
                var assetsDest = Path.ChangeExtension(dest, null) + ".assets";
                Directory.Move(assets, assetsDest);
            }

            _history.SnapshotIfChanged(id, note.Markdown);
            _vault.Rescan();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trash failed");
            return false;
        }
    }

    public NoteSaveResult RestoreHistory(Guid id, string snapshotId)
    {
        var existing = _vault.GetNote(id);
        if (existing is null) return Fail("Note not found");
        var snap = _history.ReadSnapshot(id, snapshotId);
        if (snap is null) return Fail("Snapshot not found");
        return Save(id, snap, existing.ETag);
    }

    public NoteMoveResult Move(Guid id, string targetFolderRelativePath, string? newTitle = null)
    {
        if (!_paths.IsConfigured)
            return new NoteMoveResult { Success = false, Error = "Vault not configured" };

        var note = _vault.GetNote(id);
        if (note is null)
            return new NoteMoveResult { Success = false, Error = "Note not found" };

        try
        {
            var oldAbs = _paths.EnsureInsideVault(note.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var oldStem = Path.GetFileNameWithoutExtension(oldAbs);
            var title = string.IsNullOrWhiteSpace(newTitle) ? note.Title : newTitle.Trim();
            var newStem = SanitizeFileName(title);

            var folder = (targetFolderRelativePath ?? "").Replace('\\', '/').Trim('/');
            var dir = string.IsNullOrEmpty(folder)
                ? _paths.VaultRoot
                : _paths.EnsureInsideVault(folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);

            var destMd = Path.Combine(dir, newStem + ".md");
            _paths.EnsureInsideVault(destMd);
            if (!string.Equals(oldAbs, destMd, StringComparison.OrdinalIgnoreCase) && File.Exists(destMd))
                return new NoteMoveResult { Success = false, Error = "A note with that name already exists in the destination" };

            var markdown = note.Markdown;
            if (!string.Equals(oldStem, newStem, StringComparison.Ordinal))
                markdown = MarkdownLinkRewriter.RewriteAssetStem(markdown, oldStem, newStem);

            if (!string.IsNullOrWhiteSpace(newTitle) && !string.Equals(newTitle, note.Title, StringComparison.Ordinal))
                markdown = UpsertFrontMatterTitle(markdown, title);

            // Write rewritten content first if stem/title changed while path stays same, else move then write
            var oldAssets = Path.Combine(Path.GetDirectoryName(oldAbs)!, oldStem + ".assets");
            var newAssets = Path.Combine(dir, newStem + ".assets");

            if (string.Equals(oldAbs, destMd, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(markdown, note.Markdown, StringComparison.Ordinal))
                    AtomicWrite(oldAbs, markdown);
                if (!string.Equals(oldStem, newStem, StringComparison.Ordinal) && Directory.Exists(oldAssets))
                {
                    if (Directory.Exists(newAssets))
                        return new NoteMoveResult { Success = false, Error = "Assets folder already exists for new name" };
                    Directory.Move(oldAssets, newAssets);
                }
            }
            else
            {
                if (!string.Equals(markdown, note.Markdown, StringComparison.Ordinal))
                    AtomicWrite(oldAbs, markdown);

                File.Move(oldAbs, destMd);
                if (Directory.Exists(oldAssets))
                {
                    if (Directory.Exists(newAssets))
                        return new NoteMoveResult { Success = false, Error = "Assets folder already exists at destination" };
                    Directory.Move(oldAssets, newAssets);
                }
            }

            _vault.Rescan();
            return new NoteMoveResult { Success = true, Note = _vault.GetNote(id) };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Note move/rename failed");
            return new NoteMoveResult { Success = false, Error = ex.Message };
        }
    }

    public NoteDetail? Duplicate(Guid id)
    {
        if (!_paths.IsConfigured) return null;
        var note = _vault.GetNote(id);
        if (note is null) return null;

        try
        {
            var oldAbs = _paths.EnsureInsideVault(note.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var oldStem = Path.GetFileNameWithoutExtension(oldAbs);
            var dir = Path.GetDirectoryName(oldAbs)!;
            var newStem = oldStem + " copy";
            var n = 1;
            while (File.Exists(Path.Combine(dir, newStem + ".md")))
            {
                newStem = $"{oldStem} copy ({n})";
                n++;
            }

            var newId = Guid.NewGuid();
            var markdown = MarkdownLinkRewriter.RewriteAssetStem(note.Markdown, oldStem, newStem);
            markdown = UpsertFrontMatterId(markdown, newId);
            markdown = UpsertFrontMatterTitle(markdown, note.Title + " copy");

            var destMd = Path.Combine(dir, newStem + ".md");
            AtomicWrite(destMd, markdown);

            var oldAssets = Path.Combine(dir, oldStem + ".assets");
            if (Directory.Exists(oldAssets))
            {
                var newAssets = Path.Combine(dir, newStem + ".assets");
                CopyDirectory(oldAssets, newAssets);
            }

            _vault.Rescan();
            return _vault.GetNote(newId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Duplicate failed");
            return null;
        }
    }

    public AttachmentUploadResult AddAttachment(Guid id, Stream content, string originalFileName, string? contentType)
    {
        if (!_paths.IsConfigured)
            return new AttachmentUploadResult { Success = false, Error = "Vault not configured" };

        var note = _vault.GetNote(id);
        if (note is null)
            return new AttachmentUploadResult { Success = false, Error = "Note not found" };

        try
        {
            if (content.CanSeek && content.Length > _options.MaxAttachmentBytes)
                return new AttachmentUploadResult { Success = false, Error = $"File exceeds max size ({_options.MaxAttachmentBytes} bytes)" };

            var mdAbs = _paths.EnsureInsideVault(note.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var stem = Path.GetFileNameWithoutExtension(mdAbs);
            var assetsDir = Path.Combine(Path.GetDirectoryName(mdAbs)!, stem + ".assets");
            Directory.CreateDirectory(assetsDir);
            _paths.EnsureInsideVault(assetsDir);

            var safeName = MakeAttachmentFileName(originalFileName, contentType);
            var dest = Path.Combine(assetsDir, safeName);
            var n = 1;
            while (File.Exists(dest))
            {
                var baseName = Path.GetFileNameWithoutExtension(safeName);
                var ext = Path.GetExtension(safeName);
                dest = Path.Combine(assetsDir, $"{baseName}-{n}{ext}");
                n++;
            }

            safeName = Path.GetFileName(dest);
            var temp = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            long written = 0;
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    if (written > _options.MaxAttachmentBytes)
                    {
                        fs.Close();
                        File.Delete(temp);
                        return new AttachmentUploadResult { Success = false, Error = $"File exceeds max size ({_options.MaxAttachmentBytes} bytes)" };
                    }
                    fs.Write(buffer, 0, read);
                }
            }

            File.Move(temp, dest, overwrite: false);
            _vault.Rescan();
            var updated = _vault.GetNote(id);
            var att = updated?.Attachments.FirstOrDefault(a =>
                string.Equals(a.FileName, safeName, StringComparison.OrdinalIgnoreCase));

            var mdPath = EncodeSpaces(stem) + ".assets/" + EncodeSpaces(safeName);
            var ct = contentType ?? GuessContentType(safeName);
            return new AttachmentUploadResult
            {
                Success = true,
                FileName = safeName,
                MarkdownPath = mdPath,
                AttachmentId = att?.Id,
                ContentType = ct,
                IsImage = ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                Note = updated
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attachment upload failed");
            return new AttachmentUploadResult { Success = false, Error = ex.Message };
        }
    }

    private static string MakeAttachmentFileName(string original, string? contentType)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var name = Path.GetFileName(original);
        if (string.IsNullOrWhiteSpace(name) || name is "image.png" or "image.jpg" or "blob")
        {
            var ext = ExtFromContentType(contentType) ?? ".png";
            return $"{stamp}_screenshot{ext}";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "file.bin";
        if (cleaned.Length > 80)
        {
            var ext = Path.GetExtension(cleaned);
            var stemLen = Math.Min(60, Math.Max(1, cleaned.Length - ext.Length));
            cleaned = cleaned[..stemLen].TrimEnd() + ext;
        }

        return $"{stamp}_{cleaned}";
    }

    private static string? ExtFromContentType(string? ct) => ct?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "application/pdf" => ".pdf",
        _ => null
    };

    private static string GuessContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".html" or ".htm" => "text/html",
            ".md" => "text/markdown",
            ".json" => "application/json",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };

    private static string EncodeSpaces(string value) => value.Replace(" ", "%20", StringComparison.Ordinal);

    public static void AtomicWrite(string absolutePath, string content)
    {
        var dir = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(absolutePath)}.{Guid.NewGuid():N}.tmp");
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(temp, content, utf8);
        _ = File.ReadAllText(temp, utf8);
        File.Move(temp, absolutePath, overwrite: true);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        foreach (var sub in Directory.EnumerateDirectories(source))
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    private static string UpsertFrontMatterTitle(string markdown, string title)
    {
        if (markdown.StartsWith("---", StringComparison.Ordinal))
        {
            var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var header = markdown[3..end];
                var body = markdown[(end + 4)..].TrimStart('\n', '\r');
                if (Regex.IsMatch(header, @"^title:\s*.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    header = Regex.Replace(header, @"^title:\s*.*$", "title: " + EscapeYaml(title), RegexOptions.Multiline | RegexOptions.IgnoreCase);
                else
                    header = header.TrimEnd() + "\ntitle: " + EscapeYaml(title) + "\n";
                return "---" + header + "\n---\n\n" + body;
            }
        }

        return markdown;
    }

    private static string UpsertFrontMatterId(string markdown, Guid id)
    {
        if (markdown.StartsWith("---", StringComparison.Ordinal))
        {
            var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var header = markdown[3..end];
                var body = markdown[(end + 4)..].TrimStart('\n', '\r');
                if (Regex.IsMatch(header, @"^id:\s*.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    header = Regex.Replace(header, @"^id:\s*.*$", "id: " + id.ToString("D"), RegexOptions.Multiline | RegexOptions.IgnoreCase);
                else
                    header = "\nid: " + id.ToString("D") + header;
                return "---" + header + "\n---\n\n" + body;
            }
        }

        return markdown;
    }

    private static NoteSaveResult Fail(string error) => new()
    {
        Success = false,
        ETag = "",
        Error = error
    };

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string EscapeYaml(string title) =>
        title.Contains(':') || title.Contains('#') || title.Contains('"')
            ? "\"" + title.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : title;

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '-' : ch);
        var name = sb.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" };
        if (reserved.Contains(name)) name += "-note";
        if (name.Length > 80) name = name[..80].TrimEnd();
        return name;
    }
}
