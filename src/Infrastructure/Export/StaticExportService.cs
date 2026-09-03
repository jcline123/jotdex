using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Export;

public sealed class StaticExportResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? ExportPath { get; init; }
    public int NoteCount { get; init; }
    public int AttachmentCount { get; init; }
}

public interface IStaticExportService
{
    StaticExportResult Export();
}

public sealed partial class StaticExportService : IStaticExportService
{
    private readonly IVaultService _vault;
    private readonly IVaultPathGuard _paths;
    private readonly IDataRootResolver _dataRoot;
    private readonly IMarkdownRenderer _markdown;
    private readonly ILogger<StaticExportService> _logger;

    public StaticExportService(
        IVaultService vault,
        IVaultPathGuard paths,
        IDataRootResolver dataRoot,
        IMarkdownRenderer markdown,
        ILogger<StaticExportService> logger)
    {
        _vault = vault;
        _paths = paths;
        _dataRoot = dataRoot;
        _markdown = markdown;
        _logger = logger;
    }

    public StaticExportResult Export()
    {
        if (!_paths.IsConfigured)
            return new StaticExportResult { Success = false, Error = "Vault not configured" };

        var exportRoot = Path.Combine(_dataRoot.ResolveDataRoot(), "exports", "static");
        var staging = Path.Combine(_dataRoot.ResolveDataRoot(), "exports", "static.tmp-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            var notes = _vault.ListNotes(null, includeSnippetNotes: true);
            var searchIndex = new List<object>();
            var attachmentCount = 0;
            var notePages = new List<(NoteSummary Summary, string HtmlRel)>();

            foreach (var summary in notes)
            {
                var detail = _vault.GetNote(summary.Id);
                if (detail is null) continue;

                var htmlRel = ToHtmlRelativePath(summary.RelativePath);
                var htmlAbs = Path.Combine(staging, htmlRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(htmlAbs)!);

                // Copy sibling .assets
                var mdAbs = _paths.EnsureInsideVault(summary.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var stem = Path.GetFileNameWithoutExtension(mdAbs);
                var assetsSrc = Path.Combine(Path.GetDirectoryName(mdAbs)!, stem + ".assets");
                if (Directory.Exists(assetsSrc))
                {
                    var assetsDest = Path.Combine(Path.GetDirectoryName(htmlAbs)!, stem + ".assets");
                    CopyDirectory(assetsSrc, assetsDest);
                    attachmentCount += Directory.GetFiles(assetsDest, "*", SearchOption.AllDirectories).Length;
                }

                var bodyHtml = SanitizeExportHtml(_markdown.ToHtml(StripFrontMatter(detail.Markdown)));
                bodyHtml = ShareHtmlAnonymizer.StripProductIdentifiers(bodyHtml);
                bodyHtml = RewriteAttachmentLinks(bodyHtml, summary.RelativePath, htmlRel);

                var depth = htmlRel.Count(c => c == '/');
                var rootPrefix = depth == 0 ? "./" : string.Concat(Enumerable.Repeat("../", depth));
                var page = WrapPage(detail.Title, bodyHtml, rootPrefix, BuildBreadcrumb(summary.FolderPath, rootPrefix));
                File.WriteAllText(htmlAbs, page, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                notePages.Add((summary, htmlRel));
                searchIndex.Add(new
                {
                    id = summary.Id,
                    title = summary.Title,
                    path = htmlRel,
                    folder = summary.FolderPath,
                    tags = summary.Tags,
                    text = PlainTextFromMarkdown(StripFrontMatter(detail.Markdown))
                });
            }

            WriteIndex(staging, notePages);
            var searchDir = Path.Combine(staging, "search");
            Directory.CreateDirectory(searchDir);
            File.WriteAllText(
                Path.Combine(searchDir, "index.json"),
                JsonSerializer.Serialize(searchIndex, new JsonSerializerOptions { WriteIndented = false }),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(searchDir, "search.js"), SearchJs(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(staging, "site.css"), SiteCss(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(staging, "README.txt"),
                "Jotdex static export — open index.html. For client search from file://, serve this folder with any static server.\r\n",
                Encoding.UTF8);

            // Atomic replace
            var backup = exportRoot + ".bak";
            if (Directory.Exists(backup))
                Directory.Delete(backup, recursive: true);
            if (Directory.Exists(exportRoot))
                Directory.Move(exportRoot, backup);
            Directory.Move(staging, exportRoot);
            if (Directory.Exists(backup))
            {
                try { Directory.Delete(backup, recursive: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not remove previous export backup"); }
            }

            _logger.LogInformation("Static export written with {Notes} notes", notePages.Count);
            return new StaticExportResult
            {
                Success = true,
                ExportPath = exportRoot,
                NoteCount = notePages.Count,
                AttachmentCount = attachmentCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Static export failed");
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch { /* ignore */ }

            return new StaticExportResult { Success = false, Error = ex.Message };
        }
    }

    private static string ToHtmlRelativePath(string mdRelativePath)
    {
        var p = mdRelativePath.Replace('\\', '/');
        if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            p = p[..^3] + ".html";
        return p;
    }

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

    private static string SanitizeExportHtml(string html)
    {
        // Drop script/iframe from rendered HTML (defense in depth for raw HTML in notes)
        html = ScriptBlockRegex().Replace(html, "");
        html = EventHandlerRegex().Replace(html, "");
        return html;
    }

    private static string RewriteAttachmentLinks(string html, string mdRelativePath, string htmlRel)
    {
        // Vault HTML often uses /api/attachments/{id}; rewrite to relative .assets paths when possible is hard without id map.
        // Relative markdown links like Note.assets/x.png already work after copy.
        _ = mdRelativePath;
        _ = htmlRel;
        return html;
    }

    private static string BuildBreadcrumb(string folderPath, string rootPrefix)
    {
        var sb = new StringBuilder();
        sb.Append($"<a href=\"{rootPrefix}index.html\">Home</a>");
        if (string.IsNullOrWhiteSpace(folderPath)) return sb.ToString();
        var parts = folderPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            sb.Append($" / <span>{WebUtility.HtmlEncode(part)}</span>");
        return sb.ToString();
    }

    private static void WriteIndex(string staging, List<(NoteSummary Summary, string HtmlRel)> notes)
    {
        var byFolder = notes
            .GroupBy(n => n.Summary.FolderPath ?? "")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("<title>Jotdex export</title>");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"./site.css\" />");
        sb.AppendLine("<script defer src=\"./search/search.js\"></script>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<header class=\"top\"><h1>Jotdex</h1><p class=\"lede\">Static read-only export</p>");
        sb.AppendLine("<label class=\"search\">Search <input id=\"q\" type=\"search\" placeholder=\"Filter notes…\" autocomplete=\"off\" /></label>");
        sb.AppendLine("<ul id=\"hits\" class=\"hits\" hidden></ul></header>");
        sb.AppendLine("<main>");

        foreach (var g in byFolder)
        {
            var folder = string.IsNullOrEmpty(g.Key) ? "(root)" : g.Key;
            sb.AppendLine($"<section><h2>{WebUtility.HtmlEncode(folder)}</h2><ul class=\"note-list\">");
            foreach (var (summary, htmlRel) in g.OrderBy(x => x.Summary.Title, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    $"<li data-title=\"{WebUtility.HtmlEncode(summary.Title)}\"><a href=\"./{htmlRel}\">{WebUtility.HtmlEncode(summary.Title)}</a></li>");
            }
            sb.AppendLine("</ul></section>");
        }

        sb.AppendLine("</main>");
        sb.AppendLine($"<footer><p>{notes.Count} notes · generated {DateTimeOffset.UtcNow:u}</p></footer>");
        sb.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(staging, "index.html"), sb.ToString(), new UTF8Encoding(false));
    }

    private static string WrapPage(string title, string bodyHtml, string rootPrefix, string breadcrumb)
    {
        var copyScript =
            """
            <script>
            document.querySelectorAll('pre').forEach(pre => {
              const btn = document.createElement('button');
              btn.type = 'button';
              btn.className = 'copy';
              btn.textContent = 'Copy';
              btn.addEventListener('click', async () => {
                try { await navigator.clipboard.writeText(pre.innerText); btn.textContent = 'Copied'; setTimeout(() => btn.textContent = 'Copy', 1200); } catch {}
              });
              pre.parentElement?.insertBefore(btn, pre);
            });
            </script>
            """;

        return
            "<!DOCTYPE html>\n<html lang=\"en\"><head>\n" +
            "<meta charset=\"utf-8\" />\n" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "<meta name=\"referrer\" content=\"no-referrer\" />\n" +
            $"<title>{WebUtility.HtmlEncode(title)}</title>\n" +
            $"<link rel=\"stylesheet\" href=\"{rootPrefix}site.css\" />\n" +
            "</head><body>\n" +
            $"<nav class=\"crumb\">{breadcrumb}</nav>\n" +
            "<article class=\"note\">\n" +
            $"<h1>{WebUtility.HtmlEncode(title)}</h1>\n" +
            bodyHtml + "\n</article>\n" +
            copyScript +
            "\n</body></html>\n";
    }

    private static string PlainTextFromMarkdown(string md)
    {
        var t = CodeFenceRegex().Replace(md, " ");
        t = LinkRegex().Replace(t, "$1");
        t = TagRegex().Replace(t, " ");
        t = WhitespaceRegex().Replace(t, " ").Trim();
        return t.Length > 4000 ? t[..4000] : t;
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string SiteCss() => """
        :root { color-scheme: light; --bg:#f6f3ee; --ink:#1a1a1a; --muted:#5c5c5c; --line:#d9d2c6; --accent:#0f5c4c; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: "Segoe UI", system-ui, sans-serif; background: var(--bg); color: var(--ink); line-height: 1.5; }
        .top, .note, footer, nav.crumb { max-width: 48rem; margin: 0 auto; padding: 1.25rem 1.25rem 0; }
        h1 { font-size: 1.8rem; margin: 0 0 0.5rem; }
        h2 { font-size: 1.1rem; margin: 1.5rem 0 0.5rem; color: var(--muted); }
        .lede { color: var(--muted); margin: 0 0 1rem; }
        .note-list { list-style: none; padding: 0; margin: 0; }
        .note-list a { color: var(--accent); text-decoration: none; }
        .note-list a:hover { text-decoration: underline; }
        .search { display: block; margin: 1rem 0; }
        .search input { width: 100%; padding: 0.55rem 0.7rem; border: 1px solid var(--line); border-radius: 8px; }
        .hits { list-style: none; padding: 0; margin: 0 0 1rem; }
        .hits a { color: var(--accent); }
        article.note { padding-bottom: 3rem; }
        pre { background: #1e1e1e; color: #f2f2f2; padding: 0.9rem; overflow: auto; border-radius: 8px; }
        code { font-family: ui-monospace, Consolas, monospace; }
        button.copy { margin: 0.5rem 0 0.25rem; }
        nav.crumb { font-size: 0.9rem; color: var(--muted); }
        nav.crumb a { color: var(--accent); }
        img { max-width: 100%; height: auto; }
        footer { color: var(--muted); font-size: 0.85rem; padding-bottom: 2rem; }
        """ + ExportCalloutCss.Rules;

    private static string SearchJs() => """
        async function boot() {
          const input = document.getElementById('q');
          const hits = document.getElementById('hits');
          if (!input || !hits) return;
          let data = [];
          try {
            const res = await fetch('./search/index.json');
            data = await res.json();
          } catch {
            input.placeholder = 'Search needs a local static server (file:// blocked)';
            input.disabled = true;
            return;
          }
          input.addEventListener('input', () => {
            const q = input.value.trim().toLowerCase();
            if (!q) { hits.hidden = true; hits.innerHTML = ''; return; }
            const words = q.split(/\s+/).filter(Boolean);
            const matched = data.filter(n => {
              const hay = `${n.title} ${n.folder} ${(n.tags||[]).join(' ')} ${n.text||[]}`.toLowerCase();
              return words.every(w => hay.includes(w));
            }).slice(0, 40);
            hits.innerHTML = matched.map(n => `<li><a href="./${n.path}">${escapeHtml(n.title)}</a></li>`).join('');
            hits.hidden = matched.length === 0;
          });
        }
        function escapeHtml(s) {
          return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
        }
        boot();
        """;

    [GeneratedRegex(@"<\s*script\b[^>]*>[\s\S]*?<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EventHandlerRegex();

    [GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Compiled)]
    private static partial Regex CodeFenceRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
