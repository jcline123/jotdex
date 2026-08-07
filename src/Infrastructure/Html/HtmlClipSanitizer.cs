using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Html;

/// <summary>Conservative HTML sanitizer for preserve-page web clips (no script execution).</summary>
public static partial class HtmlClipSanitizer
{
    public static string SanitizeFragment(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var s = html;
        // Drop dangerous elements (including content)
        s = BlockElementRegex().Replace(s, "");
        s = VoidDangerousRegex().Replace(s, "");
        // Drop inline event handlers
        s = EventHandlerRegex().Replace(s, "");
        // Neutralize javascript: / data:text/html / vbscript:
        s = UnsafeUrlRegex().Replace(s, "$1\"#blocked\"");
        // Strip style attributes (can contain expressions)
        s = StyleAttrRegex().Replace(s, "");
        return s.Trim();
    }

    public static string WrapDocument(string sanitizedFragment, string? sourceUrl, string title = "Clipped page")
    {
        var safeTitle = WebUtility.HtmlEncode(title);
        var sourceLine = string.IsNullOrWhiteSpace(sourceUrl)
            ? ""
            : $"<p class=\"jotdex-clip-source\">Source: <a href=\"{WebUtility.HtmlEncode(sourceUrl)}\">{WebUtility.HtmlEncode(sourceUrl)}</a></p>\n";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine($"<meta name=\"referrer\" content=\"no-referrer\" />");
        sb.AppendLine($"<title>{safeTitle}</title>");
        sb.AppendLine("<style>body{font-family:system-ui,sans-serif;line-height:1.45;max-width:52rem;margin:1.5rem auto;padding:0 1rem;color:#111} img{max-width:100%} .jotdex-clip-source{font-size:.9rem;color:#555}</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append(sourceLine);
        sb.AppendLine(sanitizedFragment);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    [GeneratedRegex(@"<\s*(script|iframe|object|embed|form|link|meta|base|svg|math|frame|frameset|applet)(\s[^>]*)?>[\s\S]*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BlockElementRegex();

    [GeneratedRegex(@"<\s*(script|iframe|object|embed|form|link|meta|base|svg|math|frame|frameset|applet)(\s[^>]*)?/\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VoidDangerousRegex();

    [GeneratedRegex(@"\s+on[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EventHandlerRegex();

    [GeneratedRegex(@"(href|src|xlink:href|action)\s*=\s*(""|')?\s*(javascript|vbscript|data\s*:\s*text\s*/\s*html)\s*:[^""'\s>]*(""|')?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnsafeUrlRegex();

    [GeneratedRegex(@"\s+style\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StyleAttrRegex();
}
