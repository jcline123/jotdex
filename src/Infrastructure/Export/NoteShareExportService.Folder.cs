using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Export;

public sealed class FolderShareExportResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? FileName { get; init; }
    public byte[]? Zip { get; init; }
    public int NoteCount { get; init; }
}

public sealed partial class NoteShareExportService
{
    public FolderShareExportResult ExportFolderZip(string? folderRelativePath)
    {
        var folder = (folderRelativePath ?? "").Replace('\\', '/').Trim().Trim('/');
        if (folder.Contains("..", StringComparison.Ordinal))
            return new FolderShareExportResult { Success = false, Error = "Invalid folder." };

        var notes = _vault.ListNotes(folder);
        if (notes.Count == 0)
            return new FolderShareExportResult { Success = false, Error = "That folder has no notes to share." };

        var prefix = string.IsNullOrEmpty(folder) ? "" : folder + "/";
        var byVaultPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var titles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in notes)
        {
            var vaultPath = summary.RelativePath.Replace('\\', '/').TrimStart('/');
            var htmlPath = ToExportHtmlPath(vaultPath, prefix);
            if (string.IsNullOrEmpty(htmlPath) || htmlPath.Contains("..", StringComparison.Ordinal))
                continue;
            byVaultPath[vaultPath] = htmlPath;
            if (!titles.TryGetValue(summary.Title, out var list))
            {
                list = [];
                titles[summary.Title] = list;
            }
            list.Add(htmlPath);
        }

        var uniqueTitles = titles
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.OrdinalIgnoreCase);

        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var indexItems = new List<(string Title, string FolderLabel, string HtmlPath)>();

                foreach (var summary in notes)
                {
                    var vaultPath = summary.RelativePath.Replace('\\', '/').TrimStart('/');
                    if (!byVaultPath.TryGetValue(vaultPath, out var htmlPath))
                        continue;

                    var exported = ExportSelfContainedHtml(summary.Id);
                    if (!exported.Success || string.IsNullOrEmpty(exported.Html))
                        continue;

                    var rewritten = RewriteInternalNoteLinks(exported.Html, vaultPath, htmlPath, byVaultPath, uniqueTitles);
                    var entry = zip.CreateEntry(htmlPath.Replace('\\', '/'), CompressionLevel.Fastest);
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                        writer.Write(rewritten);

                    var folderLabel = SubfolderLabel(vaultPath, prefix);
                    indexItems.Add((summary.Title, folderLabel, htmlPath));
                }

                if (indexItems.Count == 0)
                    return new FolderShareExportResult { Success = false, Error = "Could not export any notes in that folder." };

                var indexHtml = BuildFolderIndex(folder, indexItems);
                var indexEntry = zip.CreateEntry("index.html", CompressionLevel.Fastest);
                using (var writer = new StreamWriter(indexEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                    writer.Write(indexHtml);
            }

            var leaf = string.IsNullOrEmpty(folder) ? "Notes" : folder.Split('/').Last();
            var zipName = SanitizeFileName(leaf);
            if (string.IsNullOrWhiteSpace(zipName))
                zipName = "notes";

            return new FolderShareExportResult
            {
                Success = true,
                FileName = zipName + "-share.zip",
                Zip = ms.ToArray(),
                NoteCount = byVaultPath.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Folder share export failed for {Folder}", folder);
            return new FolderShareExportResult { Success = false, Error = ex.Message };
        }
    }

    private string BuildFolderIndex(string folder, List<(string Title, string FolderLabel, string HtmlPath)> items)
    {
        var title = string.IsNullOrEmpty(folder) ? "Notes" : folder.Split('/').Last();
        var sb = new StringBuilder();
        sb.Append("<p class=\"tags\"><span>Folder share</span></p>");
        sb.Append("<p>Open a note below. Links between notes in this export open the matching HTML file.</p>");

        foreach (var group in items
                     .GroupBy(i => i.FolderLabel)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var heading = string.IsNullOrEmpty(group.Key) ? "This folder" : group.Key;
            sb.Append("<h2>").Append(WebUtility.HtmlEncode(heading)).Append("</h2><ul>");
            foreach (var item in group.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("<li><a href=\"")
                    .Append(WebUtility.HtmlEncode(EncodeHrefPath(item.HtmlPath)))
                    .Append("\">")
                    .Append(WebUtility.HtmlEncode(item.Title))
                    .Append("</a></li>");
            }
            sb.Append("</ul>");
        }

        return WrapPage(title, sb.ToString(), Array.Empty<string>());
    }

    private static string RewriteInternalNoteLinks(
        string html,
        string fromVaultPath,
        string fromHtmlPath,
        IReadOnlyDictionary<string, string> byVaultPath,
        IReadOnlyDictionary<string, string> uniqueTitles)
    {
        html = HrefRegex().Replace(html, match =>
        {
            var quote = match.Groups[1].Value;
            var href = WebUtility.HtmlDecode(match.Groups[2].Value);
            if (!TryResolveExportedNote(href, fromVaultPath, byVaultPath, uniqueTitles, out var targetHtml, out var hash))
                return match.Value;
            var rel = RelativeHref(fromHtmlPath, targetHtml) + hash;
            return "href=" + quote + WebUtility.HtmlEncode(rel) + quote;
        });

        return WikiTextRegex().Replace(html, match =>
        {
            var title = match.Groups[1].Value.Trim();
            var label = match.Groups[2].Success ? match.Groups[2].Value.Trim() : title;
            if (!uniqueTitles.TryGetValue(title, out var targetHtml))
                return match.Value;
            var rel = RelativeHref(fromHtmlPath, targetHtml);
            return $"<a href=\"{WebUtility.HtmlEncode(rel)}\">{WebUtility.HtmlEncode(label)}</a>";
        });
    }

    private static bool TryResolveExportedNote(
        string href,
        string fromVaultPath,
        IReadOnlyDictionary<string, string> byVaultPath,
        IReadOnlyDictionary<string, string> uniqueTitles,
        out string targetHtml,
        out string hash)
    {
        targetHtml = "";
        hash = "";
        var raw = href.Trim();
        if (raw.Length == 0) return false;
        if (raw.StartsWith('#')) return false;
        if (raw.StartsWith('/')) return false;
        if (Regex.IsMatch(raw, @"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)) return false;

        var hashAt = raw.IndexOf('#');
        if (hashAt >= 0)
        {
            hash = raw[hashAt..];
            raw = raw[..hashAt];
        }
        var q = raw.IndexOf('?');
        if (q >= 0) raw = raw[..q];
        raw = Uri.UnescapeDataString(raw).Replace('\\', '/').Trim();
        if (raw.Length == 0 || raw.Contains(".assets/", StringComparison.OrdinalIgnoreCase))
            return false;

        var resolved = ResolveRelativeNotePath(fromVaultPath, raw);
        if (byVaultPath.TryGetValue(resolved, out var mapped))
        {
            targetHtml = mapped;
            return true;
        }
        if (!resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            byVaultPath.TryGetValue(resolved + ".md", out mapped))
        {
            targetHtml = mapped;
            return true;
        }

        var titleGuess = Path.GetFileNameWithoutExtension(resolved);
        if (uniqueTitles.TryGetValue(titleGuess, out mapped))
        {
            targetHtml = mapped;
            return true;
        }
        return false;
    }

    private static string ResolveRelativeNotePath(string fromVaultPath, string href)
    {
        var clean = href;
        while (clean.StartsWith("./", StringComparison.Ordinal)) clean = clean[2..];
        var stack = new List<string>();
        var fromDir = Path.GetDirectoryName(fromVaultPath.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
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

    private static string RelativeHref(string fromHtmlPath, string toHtmlPath)
    {
        var fromDir = Path.GetDirectoryName(fromHtmlPath.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        var rel = Path.GetRelativePath(
            string.IsNullOrEmpty(fromDir) ? "." : fromDir,
            toHtmlPath.Replace('/', Path.DirectorySeparatorChar)).Replace('\\', '/');
        return EncodeHrefPath(rel);
    }

    private static string EncodeHrefPath(string path) =>
        string.Join('/', path.Split('/').Select(seg =>
            seg is "." or ".." ? seg : Uri.EscapeDataString(seg)));

    private static string ToExportHtmlPath(string vaultPath, string prefix)
    {
        var rest = vaultPath;
        if (!string.IsNullOrEmpty(prefix) &&
            rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rest = rest[prefix.Length..];
        if (rest.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            rest = rest[..^3] + ".html";
        return rest.TrimStart('/');
    }

    private static string SubfolderLabel(string vaultPath, string prefix)
    {
        var rest = vaultPath;
        if (!string.IsNullOrEmpty(prefix) && rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rest = rest[prefix.Length..];
        var dir = Path.GetDirectoryName(rest.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? "";
        return dir;
    }

    [GeneratedRegex(@"href\s*=\s*([""'])([^""']+)\1", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefRegex();

    [GeneratedRegex(@"\[\[([^\]|#]+?)(?:#[^\]|]*)?(?:\|([^\]]+))?\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiTextRegex();
}
