using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

public sealed class BacklinkHit
{
    public required Guid NoteId { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderPath { get; init; }
    public string? Context { get; init; }
}

public sealed class NoteIndexEntry
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderPath { get; init; }
}

public interface INoteLinkService
{
    IReadOnlyList<NoteIndexEntry> GetIndex();
    IReadOnlyList<BacklinkHit> GetBacklinks(Guid noteId);
    string? RelativeMarkdownPath(string fromNoteRelativePath, string toNoteRelativePath);
}

public sealed partial class NoteLinkService : INoteLinkService
{
    private readonly IVaultService _vault;
    private readonly ILogger<NoteLinkService> _logger;

    public NoteLinkService(IVaultService vault, ILogger<NoteLinkService> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    public IReadOnlyList<NoteIndexEntry> GetIndex() =>
        _vault.ListNotes(null)
            .Select(n => new NoteIndexEntry
            {
                Id = n.Id,
                Title = n.Title,
                RelativePath = n.RelativePath,
                FolderPath = n.FolderPath
            })
            .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<BacklinkHit> GetBacklinks(Guid noteId)
    {
        var target = _vault.GetNote(noteId);
        if (target is null) return [];

        var notes = _vault.ListNotes(null);
        var byTitle = notes
            .GroupBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hits = new List<BacklinkHit>();
        var targetPathNorm = NormalizePath(target.RelativePath);
        var targetTitle = target.Title;

        foreach (var summary in notes)
        {
            if (summary.Id == noteId) continue;
            var detail = _vault.GetNote(summary.Id);
            if (detail is null) continue;
            var body = StripFrontMatter(detail.Markdown);

            var matched = false;
            string? context = null;

            foreach (Match m in MdLinkRegex().Matches(body))
            {
                var href = m.Groups[1].Value.Trim();
                if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.Contains(".assets/", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var resolved = ResolveLink(summary.RelativePath, href);
                    if (string.Equals(resolved, targetPathNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        context = SnipAround(body, m.Index, 80);
                        break;
                    }
                }
                catch
                {
                    // ignore bad links
                }
            }

            if (!matched)
            {
                foreach (Match m in WikiLinkRegex().Matches(body))
                {
                    var title = m.Groups[1].Value.Trim();
                    if (!title.Equals(targetTitle, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!byTitle.ContainsKey(title)) continue;
                    matched = true;
                    context = SnipAround(body, m.Index, 80);
                    break;
                }
            }

            if (matched)
            {
                hits.Add(new BacklinkHit
                {
                    NoteId = summary.Id,
                    Title = summary.Title,
                    RelativePath = summary.RelativePath,
                    FolderPath = summary.FolderPath,
                    Context = context
                });
            }
        }

        return hits.OrderBy(h => h.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? RelativeMarkdownPath(string fromNoteRelativePath, string toNoteRelativePath)
    {
        try
        {
            var fromDir = Path.GetDirectoryName(fromNoteRelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
            var toFull = toNoteRelativePath.Replace('/', Path.DirectorySeparatorChar);
            var fromBase = string.IsNullOrEmpty(fromDir) ? "." : fromDir;
            var rel = Path.GetRelativePath(fromBase, toFull).Replace('\\', '/');
            if (!rel.StartsWith('.')) rel = "./" + rel;
            return EncodePathSegments(rel);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Relative path failed");
            return null;
        }
    }

    private static string ResolveLink(string fromNoteRelativePath, string href)
    {
        var clean = Uri.UnescapeDataString(href.Split('#', 2)[0].Split('?', 2)[0].Trim()).Replace('\\', '/');
        while (clean.StartsWith("./", StringComparison.Ordinal)) clean = clean[2..];

        var stack = new List<string>();
        var fromDir = Path.GetDirectoryName(fromNoteRelativePath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        if (!string.IsNullOrEmpty(fromDir))
            stack.AddRange(fromDir.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var seg in clean.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }

        return string.Join('/', stack);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string EncodePathSegments(string path) =>
        string.Join('/', path.Split('/').Select(seg =>
            seg is "." or ".." ? seg : Uri.EscapeDataString(seg)));

    private static string StripFrontMatter(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal)) return markdown;
        var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return markdown;
        var after = end + 4;
        if (after < markdown.Length && markdown[after] == '\r') after++;
        if (after < markdown.Length && markdown[after] == '\n') after++;
        return markdown[after..];
    }

    private static string SnipAround(string body, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var end = Math.Min(body.Length, index + radius);
        var snip = body[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (start > 0) snip = "…" + snip;
        if (end < body.Length) snip += "…";
        return snip;
    }

    [GeneratedRegex(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MdLinkRegex();

    [GeneratedRegex(@"\[\[([^\]|#]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkRegex();
}
