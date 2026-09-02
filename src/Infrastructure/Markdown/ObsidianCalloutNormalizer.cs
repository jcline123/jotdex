using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Markdown;

/// <summary>
/// Markdig's GitHub-alert parser only accepts <c>&gt; [!type]</c> as the whole first line.
/// Obsidian also allows a title on that line (<c>&gt; [!warning] Watch out</c>). Split
/// the title onto the next quote line so Share HTML / static export keep a typed alert.
/// </summary>
internal static partial class ObsidianCalloutNormalizer
{
    public static string NormalizeTitledMarkers(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || markdown.IndexOf("[!", StringComparison.Ordinal) < 0)
            return markdown;

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(markdown.Length + 32);
        var fenceChar = '\0';
        var fenceLen = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryToggleFence(line, ref fenceChar, ref fenceLen))
            {
                AppendLine(sb, line, i < lines.Length - 1);
                continue;
            }

            if (fenceChar == '\0')
            {
                var m = TitledMarker().Match(line);
                if (m.Success)
                {
                    AppendLine(sb, $"> [!{m.Groups[1].Value}]", keepNewline: true);
                    AppendLine(sb, $"> {m.Groups[2].Value}", i < lines.Length - 1);
                    continue;
                }
            }

            AppendLine(sb, line, i < lines.Length - 1);
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string line, bool keepNewline)
    {
        sb.Append(line);
        if (keepNewline) sb.Append('\n');
    }

    private static bool TryToggleFence(string line, ref char fenceChar, ref int fenceLen)
    {
        var span = line.AsSpan();
        var i = 0;
        while (i < span.Length && (span[i] == ' ' || span[i] == '\t')) i++;
        if (i >= span.Length) return false;
        var ch = span[i];
        if (ch is not ('`' or '~')) return false;
        var n = 0;
        while (i + n < span.Length && span[i + n] == ch) n++;
        if (n < 3) return false;

        if (fenceChar == '\0')
        {
            fenceChar = ch;
            fenceLen = n;
            return true;
        }

        if (ch == fenceChar && n >= fenceLen)
        {
            fenceChar = '\0';
            fenceLen = 0;
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"^>\s*\[!(\w+)\]\s+(\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TitledMarker();
}
