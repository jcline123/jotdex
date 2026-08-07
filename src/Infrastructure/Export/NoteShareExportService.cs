using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Export;

public sealed class NoteShareExportResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? FileName { get; init; }
    public string? Html { get; init; }
    public int EmbeddedImages { get; init; }
}

public interface INoteShareExportService
{
    NoteShareExportResult ExportSelfContainedHtml(Guid noteId);
}

/// <summary>
/// Builds a single self-contained HTML file for one note, with images inlined as data URIs
/// so it can be emailed or opened offline without the vault.
/// </summary>
public sealed partial class NoteShareExportService : INoteShareExportService
{
    private readonly IVaultService _vault;
    private readonly ILogger<NoteShareExportService> _logger;

    public NoteShareExportService(IVaultService vault, ILogger<NoteShareExportService> logger)
    {
        _vault = vault;
        _logger = logger;
    }

    public NoteShareExportResult ExportSelfContainedHtml(Guid noteId)
    {
        var note = _vault.GetNote(noteId);
        if (note is null)
            return new NoteShareExportResult { Success = false, Error = "Note not found" };

        try
        {
            // Vault HTML already rewrites local images to /api/attachments/{id}.
            var body = Sanitize(note.Html);
            var embedded = 0;

            foreach (var att in note.Attachments)
            {
                if (!IsImage(att.ContentType, att.FileName))
                    continue;

                string dataUri;
                try
                {
                    dataUri = ToDataUri(att);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not embed attachment {File}", att.FileName);
                    continue;
                }

                var api = $"/api/attachments/{att.Id}";
                var next = body
                    .Replace($"src=\"{api}\"", $"src=\"{dataUri}\"", StringComparison.OrdinalIgnoreCase)
                    .Replace($"src='{api}'", $"src='{dataUri}'", StringComparison.OrdinalIgnoreCase);

                // Relative markdown asset forms that may remain in raw HTML blocks
                var stem = Path.GetFileNameWithoutExtension(note.RelativePath.Replace('\\', '/'));
                foreach (var candidate in RelativeAssetCandidates(stem, att.FileName))
                {
                    next = next
                        .Replace($"src=\"{candidate}\"", $"src=\"{dataUri}\"", StringComparison.OrdinalIgnoreCase)
                        .Replace($"src='{candidate}'", $"src='{dataUri}'", StringComparison.OrdinalIgnoreCase);
                }

                if (next != body)
                {
                    body = next;
                    embedded++;
                }
            }

            var html = WrapPage(note.Title, body, note.Tags);
            var safeName = SanitizeFileName(note.Title);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = note.Id.ToString("N")[..8];

            return new NoteShareExportResult
            {
                Success = true,
                FileName = safeName + ".html",
                Html = html,
                EmbeddedImages = embedded
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Share export failed for {Id}", noteId);
            return new NoteShareExportResult { Success = false, Error = ex.Message };
        }
    }

    private string ToDataUri(AttachmentInfo att)
    {
        using var stream = _vault.OpenAttachmentStream(att.Id);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var mime = string.IsNullOrWhiteSpace(att.ContentType) || att.ContentType == "application/octet-stream"
            ? GuessMime(att.FileName)
            : att.ContentType;
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static IEnumerable<string> RelativeAssetCandidates(string stem, string fileName)
    {
        var enc = Uri.EscapeDataString(fileName);
        var spaceEnc = fileName.Replace(" ", "%20", StringComparison.Ordinal);
        yield return $"{stem}.assets/{fileName}";
        yield return $"./{stem}.assets/{fileName}";
        yield return $"{stem}.assets/{enc}";
        yield return $"./{stem}.assets/{enc}";
        yield return $"{stem}.assets/{spaceEnc}";
        yield return $".assets/{fileName}";
        yield return $"./.assets/{fileName}";
        yield return $".assets/{enc}";
    }

    private static bool IsImage(string contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        return Path.GetExtension(fileName).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp";
    }

    private static string GuessMime(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static string Sanitize(string html)
    {
        html = ScriptBlockRegex().Replace(html, "");
        html = EventHandlerRegex().Replace(html, "");
        return html;
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title.Trim())
        {
            if (invalid.Contains(ch) || ch < 32)
                sb.Append('-');
            else
                sb.Append(ch);
        }
        var name = WhitespaceRegex().Replace(sb.ToString(), " ").Trim(' ', '.', '-');
        return name.Length > 80 ? name[..80].Trim() : name;
    }

    private static string WrapPage(string title, string bodyHtml, IReadOnlyList<string> tags)
    {
        var tagHtml = tags.Count == 0
            ? ""
            : "<p class=\"tags\">" + string.Join(" ", tags.Select(t =>
                $"<span>{WebUtility.HtmlEncode(t)}</span>")) + "</p>";

        var copyScript =
            """
            <script>
            document.querySelectorAll('pre').forEach(pre => {
              const wrap = document.createElement('div');
              wrap.className = 'code-wrap';
              pre.parentNode.insertBefore(wrap, pre);
              wrap.appendChild(pre);
              const btn = document.createElement('button');
              btn.type = 'button';
              btn.className = 'copy';
              btn.textContent = 'Copy';
              btn.addEventListener('click', async () => {
                try {
                  await navigator.clipboard.writeText(pre.innerText);
                  btn.textContent = 'Copied';
                  setTimeout(() => btn.textContent = 'Copy', 1200);
                } catch {}
              });
              wrap.insertBefore(btn, pre);
            });
            </script>
            """;

        return
            "<!DOCTYPE html>\n<html lang=\"en\"><head>\n" +
            "<meta charset=\"utf-8\" />\n" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
            "<meta name=\"referrer\" content=\"no-referrer\" />\n" +
            $"<title>{WebUtility.HtmlEncode(title)}</title>\n" +
            "<style>\n" + ShareCss() + "\n</style>\n" +
            "</head><body>\n" +
            "<article class=\"note\">\n" +
            $"<header><p class=\"brand\">Jotdex</p><h1>{WebUtility.HtmlEncode(title)}</h1>{tagHtml}</header>\n" +
            "<div class=\"body\">\n" + bodyHtml + "\n</div>\n" +
            "</article>\n" +
            copyScript +
            "\n</body></html>\n";
    }

    private static string ShareCss() => """
        :root { color-scheme: light; --bg:#f4f1ea; --paper:#fffcf7; --ink:#1c1917; --muted:#6b6560; --line:#e4ddd2; --accent:#0f5c4c; --code:#1e1e1e; }
        * { box-sizing: border-box; }
        body { margin: 0; font-family: "Segoe UI", "Helvetica Neue", sans-serif; background:
          radial-gradient(ellipse at top, #ebe4d8 0%, var(--bg) 55%); color: var(--ink); line-height: 1.55; }
        article.note { max-width: 44rem; margin: 2rem auto; padding: 2rem 1.75rem 3rem; background: var(--paper);
          border: 1px solid var(--line); border-radius: 14px; box-shadow: 0 18px 40px rgba(28,25,23,.06); }
        .brand { margin: 0 0 .35rem; font-size: .75rem; letter-spacing: .12em; text-transform: uppercase; color: var(--muted); }
        h1 { margin: 0 0 1rem; font-size: 1.85rem; line-height: 1.2; }
        .tags { display: flex; flex-wrap: wrap; gap: .4rem; margin: 0 0 1.25rem; }
        .tags span { font-size: .75rem; padding: .15rem .55rem; border: 1px solid var(--line); border-radius: 999px; color: var(--muted); }
        .body > *:first-child { margin-top: 0; }
        h2, h3, h4 { line-height: 1.25; }
        a { color: var(--accent); }
        img { max-width: 100%; height: auto; border-radius: 8px; }
        blockquote { margin: 1rem 0; padding: .2rem 1rem; border-left: 3px solid var(--accent); color: var(--muted); }
        table { border-collapse: collapse; width: 100%; margin: 1rem 0; font-size: .95rem; }
        th, td { border: 1px solid var(--line); padding: .45rem .6rem; text-align: left; }
        .code-wrap { position: relative; margin: 1rem 0; }
        pre { background: var(--code); color: #f2f2f2; padding: .9rem 1rem; overflow: auto; border-radius: 8px; margin: 0; }
        code { font-family: ui-monospace, Consolas, monospace; font-size: .92em; }
        :not(pre) > code { background: #efe9df; padding: .1rem .35rem; border-radius: 4px; }
        button.copy { position: absolute; top: .45rem; right: .45rem; font: inherit; font-size: .75rem;
          padding: .25rem .55rem; border-radius: 6px; border: 1px solid #444; background: #2a2a2a; color: #eee; cursor: pointer; }
        ul.contains-task-list { list-style: none; padding-left: 0; }
        @media (max-width: 640px) {
          article.note { margin: 0; border-radius: 0; border-left: 0; border-right: 0; }
        }
        @media print {
          body { background: #fff; }
          article.note { box-shadow: none; border: none; margin: 0; max-width: none; }
          button.copy { display: none; }
        }
        """;

    [GeneratedRegex(@"<\s*script\b[^>]*>[\s\S]*?<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EventHandlerRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
