using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

public sealed class VaultService : IVaultService
{
    private readonly IVaultPathGuard _paths;
    private readonly IMarkdownRenderer _markdown;
    private readonly ILogger<VaultService> _logger;
    private readonly IEnumerable<IVaultRescanObserver> _observers;
    private readonly object _gate = new();
    private VaultSnapshot _snapshot = VaultSnapshot.Empty;

    public VaultService(
        IVaultPathGuard paths,
        IMarkdownRenderer markdown,
        ILogger<VaultService> logger,
        IEnumerable<IVaultRescanObserver> observers)
    {
        _paths = paths;
        _markdown = markdown;
        _logger = logger;
        _observers = observers;
    }

    public void Rescan()
    {
        if (!_paths.IsConfigured)
        {
            lock (_gate) _snapshot = VaultSnapshot.Empty;
            NotifyObservers();
            return;
        }

        var root = _paths.VaultRoot;
        _logger.LogInformation("Scanning vault (path omitted from detail logs)");

        var notes = new Dictionary<Guid, IndexedNote>();
        var attachments = new Dictionary<string, IndexedAttachment>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };

        // Include empty folders so create/rename shows up before notes exist
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var full = _paths.EnsureInsideVault(dir);
                var rel = _paths.ToRelativePath(full);
                if (string.IsNullOrEmpty(rel) || rel.Contains("/.", StringComparison.Ordinal) || rel.StartsWith('.'))
                    continue;
                if (rel.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) ||
                    rel.Contains(".assets/", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var part in ExpandFolderChain(rel))
                    folders.Add(part);
            }
            catch
            {
                // skip unreadable dirs
            }
        }

        foreach (var md in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            try
            {
                var full = _paths.EnsureInsideVault(md);
                var rel = _paths.ToRelativePath(full);
                if (rel.Contains("/.", StringComparison.Ordinal) || rel.StartsWith('.'))
                    continue;

                var text = File.ReadAllText(full, Encoding.UTF8);
                var fm = FrontMatterParser.Parse(text);
                var fileName = Path.GetFileNameWithoutExtension(full);
                var title = FrontMatterParser.DeriveTitle(fm.Fields, fm.Body, fileName) ?? fileName;
                var id = FrontMatterParser.DeriveId(fm.Fields, rel);
                var folder = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                if (folder == ".") folder = "";

                foreach (var part in ExpandFolderChain(folder))
                    folders.Add(part);

                var assetsDir = Path.Combine(Path.GetDirectoryName(full)!, fileName + ".assets");
                var noteAttachments = new List<AttachmentInfo>();
                var sidecars = new List<HtmlSidecar>();
                if (Directory.Exists(assetsDir))
                {
                    _paths.EnsureInsideVault(assetsDir);
                    foreach (var file in Directory.EnumerateFiles(assetsDir))
                    {
                        _paths.EnsureInsideVault(file);
                        var attRel = _paths.ToRelativePath(file);
                        var attId = MakeAttachmentId(id, Path.GetFileName(file));
                        var info = new AttachmentInfo
                        {
                            Id = attId,
                            FileName = Path.GetFileName(file),
                            RelativePath = attRel,
                            ContentType = GuessContentType(file),
                            SizeBytes = new FileInfo(file).Length
                        };
                        noteAttachments.Add(info);
                        attachments[attId] = new IndexedAttachment(info, file);
                        if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                        {
                            sidecars.Add(new HtmlSidecar
                            {
                                FileName = info.FileName,
                                RelativePath = attRel,
                                AttachmentId = attId
                            });
                        }
                    }
                }

                var etag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
                notes[id] = new IndexedNote(
                    new NoteSummary
                    {
                        Id = id,
                        Title = title,
                        RelativePath = rel,
                        FolderPath = folder,
                        Tags = FrontMatterParser.ParseTags(fm.Fields),
                        Modified = FrontMatterParser.ParseDate(fm.Fields, "modified") ?? File.GetLastWriteTimeUtc(full),
                        Created = FrontMatterParser.ParseDate(fm.Fields, "created"),
                        HasAttachments = noteAttachments.Count > 0,
                        Favorite = IsFavoriteField(fm.Fields)
                    },
                    full,
                    text,
                    fm,
                    etag,
                    noteAttachments,
                    sidecars);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping note during scan");
            }
        }

        string vaultName = "Vault";
        string formatVersion = "1";
        var marker = Path.Combine(root, ".notes-vault.json");
        if (File.Exists(marker))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                if (doc.RootElement.TryGetProperty("name", out var n)) vaultName = n.GetString() ?? vaultName;
                if (doc.RootElement.TryGetProperty("formatVersion", out var v))
                    formatVersion = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString() ?? formatVersion;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read vault marker");
            }
        }

        lock (_gate)
        {
            _snapshot = new VaultSnapshot(vaultName, formatVersion, notes, attachments, folders);
        }

        NotifyObservers();
    }

    private void NotifyObservers()
    {
        foreach (var observer in _observers)
        {
            try { observer.AfterVaultRescan(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Vault rescan observer failed"); }
        }
    }

    public VaultInfo GetInfo()
    {
        var s = Snapshot;
        return new VaultInfo
        {
            Name = s.Name,
            FormatVersion = s.FormatVersion,
            NoteCount = s.Notes.Count,
            FolderCount = s.Folders.Count
        };
    }

    public FolderNode GetTree()
    {
        var s = Snapshot;
        return BuildTree(s.Folders);
    }

    public IReadOnlyList<NoteSummary> ListNotes(string? folderRelativePath, bool includeStandaloneTodosMd = false)
    {
        var s = Snapshot;
        var folder = (folderRelativePath ?? "").Replace('\\', '/').Trim('/');
        return s.Notes.Values
            .Select(n => n.Summary)
            .Where(n => string.IsNullOrEmpty(folder)
                ? true
                : n.FolderPath.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                  n.FolderPath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
            .Where(n => includeStandaloneTodosMd || !IsStandaloneTodosNote(n.RelativePath))
            .OrderByDescending(n => n.Favorite)
            .ThenByDescending(n => n.Modified ?? n.Created ?? DateTimeOffset.MinValue)
            .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsStandaloneTodosNote(string relativePath)
    {
        var rel = (relativePath ?? "").Replace('\\', '/').Trim('/');
        if (rel.Equals("Todos.md", StringComparison.OrdinalIgnoreCase)) return true;
        // Orphan duplicates from create-while-hidden race
        if (Regex.IsMatch(rel, @"^Todos \(\d+\)\.md$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;
        return false;
    }

    public NoteDetail? GetNote(Guid id)
    {
        var s = Snapshot;
        if (!s.Notes.TryGetValue(id, out var note)) return null;

        var html = RewriteAssetUrls(_markdown.ToHtml(note.Fm.Body), note.Attachments);
        return new NoteDetail
        {
            Id = note.Summary.Id,
            Title = note.Summary.Title,
            RelativePath = note.Summary.RelativePath,
            FolderPath = note.Summary.FolderPath,
            Markdown = note.RawText,
            Html = html,
            ETag = note.ETag,
            Tags = note.Summary.Tags,
            Modified = note.Summary.Modified,
            Created = note.Summary.Created,
            FrontMatter = note.Fm.Fields,
            Attachments = note.Attachments,
            HtmlSidecars = note.Sidecars
        };
    }

    public NoteDetail? GetNoteByRelativePath(string relativePath)
    {
        var want = (relativePath ?? "").Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(want)) return null;
        var s = Snapshot;
        var match = s.Notes.Values.FirstOrDefault(n =>
            n.Summary.RelativePath.Replace('\\', '/').Trim('/')
                .Equals(want, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : GetNote(match.Summary.Id);
    }

    private static string RewriteAssetUrls(string html, IReadOnlyList<AttachmentInfo> attachments)
    {
        foreach (var att in attachments)
        {
            var encoded = Uri.EscapeDataString(att.FileName);
            var plain = att.FileName;
            var api = $"/api/attachments/{att.Id}";
            html = html.Replace($"src=\"{plain}\"", $"src=\"{api}\"", StringComparison.OrdinalIgnoreCase);
            html = html.Replace($"src=\"{encoded}\"", $"src=\"{api}\"", StringComparison.OrdinalIgnoreCase);
            // Common relative forms: Note.assets/file or ./Note.assets/file
            var assetsSuffix = ".assets/" + plain;
            var assetsSuffixEnc = ".assets/" + encoded;
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                $@"src=""[^""]*{System.Text.RegularExpressions.Regex.Escape(assetsSuffix)}""",
                $"src=\"{api}\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                $@"src=""[^""]*{System.Text.RegularExpressions.Regex.Escape(assetsSuffixEnc)}""",
                $"src=\"{api}\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return html;
    }

    public AttachmentInfo? GetAttachment(string attachmentId)
    {
        var s = Snapshot;
        return s.Attachments.TryGetValue(attachmentId, out var a) ? a.Info : null;
    }

    public Stream OpenAttachmentStream(string attachmentId)
    {
        var s = Snapshot;
        if (!s.Attachments.TryGetValue(attachmentId, out var a))
            throw new FileNotFoundException("Attachment not found.");

        var full = _paths.EnsureInsideVault(a.AbsolutePath);
        return File.OpenRead(full);
    }

    private VaultSnapshot Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    private static IEnumerable<string> ExpandFolderChain(string folder)
    {
        if (string.IsNullOrEmpty(folder)) yield break;
        var parts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var acc = "";
        foreach (var p in parts)
        {
            acc = string.IsNullOrEmpty(acc) ? p : acc + "/" + p;
            yield return acc;
        }
    }

    private static FolderNode BuildTree(HashSet<string> folders)
    {
        var root = new MutableFolder { Id = "root", Name = "Notes", RelativePath = "" };
        foreach (var path in folders.Where(f => !string.IsNullOrEmpty(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var parts = path.Split('/');
            var current = root;
            var acc = "";
            foreach (var part in parts)
            {
                acc = string.IsNullOrEmpty(acc) ? part : acc + "/" + part;
                if (!current.Children.TryGetValue(part, out var child))
                {
                    child = new MutableFolder { Id = "folder:" + acc, Name = part, RelativePath = acc };
                    current.Children[part] = child;
                }
                current = child;
            }
        }

        return root.ToNode();
    }

    private static string MakeAttachmentId(Guid noteId, string fileName) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{noteId:N}:{fileName}"))).ToLowerInvariant()[..32];

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".html" or ".htm" => "text/html",
        ".json" => "application/json",
        ".txt" or ".md" => "text/plain",
        _ => "application/octet-stream"
    };

    private sealed record IndexedNote(
        NoteSummary Summary,
        string AbsolutePath,
        string RawText,
        FrontMatterResult Fm,
        string ETag,
        IReadOnlyList<AttachmentInfo> Attachments,
        IReadOnlyList<HtmlSidecar> Sidecars);

    private static bool IsFavoriteField(IReadOnlyDictionary<string, string?> fields)
    {
        if (!fields.TryGetValue("favorite", out var v) && !fields.TryGetValue("favourite", out v))
            return false;
        if (string.IsNullOrWhiteSpace(v)) return false;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record IndexedAttachment(AttachmentInfo Info, string AbsolutePath);

    private sealed class VaultSnapshot(
        string name,
        string formatVersion,
        Dictionary<Guid, IndexedNote> notes,
        Dictionary<string, IndexedAttachment> attachments,
        HashSet<string> folders)
    {
        public static VaultSnapshot Empty { get; } = new("Vault", "1", new(), new(), new() { "" });
        public string Name { get; } = name;
        public string FormatVersion { get; } = formatVersion;
        public Dictionary<Guid, IndexedNote> Notes { get; } = notes;
        public Dictionary<string, IndexedAttachment> Attachments { get; } = attachments;
        public HashSet<string> Folders { get; } = folders;
    }

    private sealed class MutableFolder
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string RelativePath { get; init; }
        public Dictionary<string, MutableFolder> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FolderNode ToNode() => new()
        {
            Id = Id,
            Name = Name,
            RelativePath = RelativePath,
            Children = Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => c.ToNode()).ToList()
        };
    }
}
