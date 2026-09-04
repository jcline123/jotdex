using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class IntegrityIssue
{
    public required string Severity { get; init; } // error | warning
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? NoteId { get; init; }
    public string? NotePath { get; init; }
    public string? Detail { get; init; }
}

public sealed class IntegrityScanResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public int NoteCount { get; init; }
    public int IssueCount => Issues.Count;
    public IReadOnlyList<IntegrityIssue> Issues { get; init; } = [];
}

public interface IIntegrityScanService
{
    IntegrityScanResult Scan();
}

public sealed partial class IntegrityScanService : IIntegrityScanService
{
    private readonly IVaultService _vault;
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<IntegrityScanService> _logger;

    public IntegrityScanService(IVaultService vault, IVaultPathGuard paths, ILogger<IntegrityScanService> logger)
    {
        _vault = vault;
        _paths = paths;
        _logger = logger;
    }

    public IntegrityScanResult Scan()
    {
        if (!_paths.IsConfigured)
            return new IntegrityScanResult { Success = false, Error = "Vault not configured" };

        var issues = new List<IntegrityIssue>();
        var notes = _vault.ListNotes(null, includeSnippetNotes: true);
        var pathByTitle = notes
            .GroupBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RelativePath).ToList(), StringComparer.OrdinalIgnoreCase);
        var pathSet = new HashSet<string>(notes.Select(n => n.RelativePath.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

        foreach (var summary in notes)
        {
            var detail = _vault.GetNote(summary.Id);
            if (detail is null) continue;

            var mdAbs = _paths.EnsureInsideVault(summary.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var noteDir = Path.GetDirectoryName(mdAbs)!;
            var stem = Path.GetFileNameWithoutExtension(mdAbs);
            var body = StripFrontMatter(detail.Markdown);

            // Missing attachment files referenced by markdown image/file links to .assets
            foreach (Match m in AssetsLinkRegex().Matches(body))
            {
                var target = Uri.UnescapeDataString(m.Groups[1].Value.Replace('\\', '/'));
                // target like Note.assets/file.png or ./Note.assets/file.png
                var cleaned = target.TrimStart('.', '/');
                var candidate = Path.GetFullPath(Path.Combine(noteDir, cleaned.Replace('/', Path.DirectorySeparatorChar)));
                try
                {
                    _paths.EnsureInsideVault(candidate);
                }
                catch
                {
                    issues.Add(Issue("error", "path-escape", summary, $"Link escapes vault: {target}"));
                    continue;
                }

                if (!File.Exists(candidate))
                    issues.Add(Issue("error", "missing-attachment", summary, $"Missing file: {target}"));
            }

            // Orphan expectation: listed attachments should exist on disk
            foreach (var att in detail.Attachments)
            {
                var abs = _paths.EnsureInsideVault(att.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs))
                    issues.Add(Issue("error", "missing-attachment", summary, $"Indexed attachment missing: {att.FileName}"));
            }

            // Broken relative .md links
            foreach (Match m in MdLinkRegex().Matches(body))
            {
                var href = m.Groups[1].Value.Trim();
                if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var pathOnly = href.Split('#', 2)[0].Split('?', 2)[0];
                if (string.IsNullOrWhiteSpace(pathOnly)) continue;
                if (!pathOnly.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                    !pathOnly.Contains(".assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (pathOnly.Contains(".assets/", StringComparison.OrdinalIgnoreCase))
                    continue; // handled above

                var decoded = Uri.UnescapeDataString(pathOnly.Replace('\\', '/'));
                var abs = Path.GetFullPath(Path.Combine(noteDir, decoded.Replace('/', Path.DirectorySeparatorChar)));
                try
                {
                    var rel = _paths.ToRelativePath(abs).Replace('\\', '/');
                    if (!File.Exists(abs) && !pathSet.Contains(rel))
                        issues.Add(Issue("warning", "broken-md-link", summary, $"Missing note link: {href}"));
                }
                catch
                {
                    issues.Add(Issue("warning", "broken-md-link", summary, $"Out-of-vault note link: {href}"));
                }
            }

            // Unresolved wikilinks (title not found)
            foreach (Match m in WikiLinkRegex().Matches(body))
            {
                var title = m.Groups[1].Value.Trim();
                if (title.Length == 0) continue;
                if (!pathByTitle.ContainsKey(title))
                    issues.Add(Issue("warning", "unresolved-wikilink", summary, $"[[{title}]]"));
            }
        }

        // Orphan .assets folders with no sibling .md
        foreach (var assetsDir in Directory.EnumerateDirectories(_paths.VaultRoot, "*.assets", SearchOption.AllDirectories))
        {
            try { _paths.EnsureInsideVault(assetsDir); }
            catch { continue; }

            var siblingMd = assetsDir[..^".assets".Length] + ".md";
            if (!File.Exists(siblingMd))
            {
                var rel = _paths.ToRelativePath(assetsDir).Replace('\\', '/');
                issues.Add(new IntegrityIssue
                {
                    Severity = "warning",
                    Code = "orphan-assets",
                    Message = $"Assets folder without sibling note: {rel}",
                    Detail = rel
                });
            }
        }

        foreach (var foldsFile in Directory.EnumerateFiles(_paths.VaultRoot, "*" + NoteFoldSidecar.FileSuffix, SearchOption.AllDirectories))
        {
            try { _paths.EnsureInsideVault(foldsFile); }
            catch { continue; }

            var siblingMd = Path.ChangeExtension(foldsFile, null);
            if (siblingMd.EndsWith(".folds", StringComparison.OrdinalIgnoreCase))
                siblingMd = siblingMd[..^".folds".Length] + ".md";
            if (!File.Exists(siblingMd))
            {
                var rel = _paths.ToRelativePath(foldsFile).Replace('\\', '/');
                issues.Add(new IntegrityIssue
                {
                    Severity = "warning",
                    Code = "orphan-folds",
                    Message = $"Heading-fold helper without sibling note: {rel}",
                    Detail = rel
                });
            }
        }

        _logger.LogInformation("Integrity scan complete: {Notes} notes, {Issues} issues", notes.Count, issues.Count);
        return new IntegrityScanResult
        {
            Success = true,
            NoteCount = notes.Count,
            Issues = issues
        };
    }

    private static IntegrityIssue Issue(string severity, string code, NoteSummary summary, string detail) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = detail,
            NoteId = summary.Id.ToString("D"),
            NotePath = summary.RelativePath,
            Detail = detail
        };

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

    [GeneratedRegex(@"!\[[^\]]*\]\(([^)]+\.assets\/[^)\s]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AssetsLinkRegex();

    [GeneratedRegex(@"\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MdLinkRegex();

    [GeneratedRegex(@"\[\[([^\]|#]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkRegex();
}
