using System.Text;
using System.Text.RegularExpressions;
using Jotdex.Core.Vault;

namespace Jotdex.Infrastructure.Search;

/// <summary>Extract plain text from vault attachments for the disposable FTS index only.</summary>
public static class AttachmentTextExtractor
{
    private static readonly HashSet<string> TextExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".csv", ".md", ".xml", ".yml", ".yaml", ".ps1", ".sh", ".css", ".js", ".ts", ".html", ".htm"
    };

    public static string Extract(IVaultService vault, IEnumerable<AttachmentInfo> attachments, int maxCharsPerFile = 80_000, int maxTotal = 200_000)
    {
        var sb = new StringBuilder();
        foreach (var att in attachments)
        {
            if (sb.Length >= maxTotal) break;
            var ext = Path.GetExtension(att.FileName);
            var isText = TextExt.Contains(ext) ||
                         att.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                         att.ContentType is "application/json" or "application/xml";
            if (!isText) continue;

            try
            {
                using var stream = vault.OpenAttachmentStream(att.Id);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var raw = reader.ReadToEnd();
                if (raw.Length > maxCharsPerFile) raw = raw[..maxCharsPerFile];
                if (ext is ".html" or ".htm")
                    raw = StripHtml(raw);
                sb.AppendLine(att.FileName);
                sb.AppendLine(raw);
            }
            catch
            {
                /* skip unreadable */
            }
        }

        return sb.ToString();
    }

    private static string StripHtml(string html)
    {
        var noScript = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        noScript = Regex.Replace(noScript, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        var text = Regex.Replace(noScript, @"<[^>]+>", " ");
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    }
}
