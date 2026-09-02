using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Markdown;

/// <summary>
/// Turns dialect v2 HTML comments / math delimiters into HTML Markdig can emit for Share/static export.
/// Skips fenced code.
/// </summary>
internal static partial class DialectV2HtmlNormalizer
{
    public static string Normalize(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = NormalizeDetails(text);
        text = NormalizeAlign(text);
        text = NormalizeMath(text);
        text = BookmarkMarker().Replace(text, "");
        return text;
    }

    private static string NormalizeDetails(string markdown)
    {
        return DetailsBlock().Replace(markdown, m =>
        {
            var inner = m.Groups[1].Value.Trim('\n');
            var split = inner.Split('\n', 2);
            var summary = split[0].Trim();
            var body = split.Length > 1 ? split[1] : "";
            return $"<details class=\"jotdex-details\"><summary>{summary}</summary>\n\n{body}\n</details>\n";
        });
    }

    private static string NormalizeAlign(string markdown)
    {
        var lines = markdown.Split('\n');
        var sb = new StringBuilder(markdown.Length + 32);
        var fence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;
            if (!fence)
            {
                var m = AlignMarker().Match(line.Trim());
                if (m.Success && i + 1 < lines.Length)
                {
                    var align = m.Groups[1].Value;
                    sb.Append("<div class=\"jotdex-align-").Append(align).Append("\">\n");
                    i++;
                    sb.Append(lines[i]).Append('\n');
                    sb.Append("</div>");
                    if (i < lines.Length - 1) sb.Append('\n');
                    continue;
                }
            }
            sb.Append(line);
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string NormalizeMath(string markdown)
    {
        var fence = false;
        var sb = new StringBuilder(markdown.Length);
        var lines = markdown.Split('\n');
        foreach (var raw in lines)
        {
            var line = raw;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fence = !fence;
            if (!fence)
            {
                line = BlockMath().Replace(line, "<div class=\"jotdex-math jotdex-math-block\">$1</div>");
                line = InlineMath().Replace(line, "<span class=\"jotdex-math jotdex-math-inline\">$1</span>");
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"<!--\s*jotdex-details\s*-->\s*([\s\S]*?)<!--\s*/jotdex-details\s*-->", RegexOptions.CultureInvariant)]
    private static partial Regex DetailsBlock();

    [GeneratedRegex(@"^<!--\s*jotdex-align:\s*(center|right|justify)\s*-->$", RegexOptions.CultureInvariant)]
    private static partial Regex AlignMarker();

    [GeneratedRegex(@"<!--\s*jotdex-link-card\s*-->\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BookmarkMarker();

    [GeneratedRegex(@"\\\[([\s\S]*?)\\\]", RegexOptions.CultureInvariant)]
    private static partial Regex BlockMath();

    [GeneratedRegex(@"\\\(([\s\S]*?)\\\)", RegexOptions.CultureInvariant)]
    private static partial Regex InlineMath();
}
