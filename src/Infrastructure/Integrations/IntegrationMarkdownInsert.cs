using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Integrations;

/// <summary>Body-only insert helpers for the integrations API (preserve surrounding body text).</summary>
public static class IntegrationMarkdownInsert
{
    private static readonly Regex HeadingLine = new(@"^(#{1,6})\s+(.+?)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Insert <paramref name="markdown"/> into note body. Front matter block is left byte-identical.
    /// Returns null when afterHeading target is missing.
    /// </summary>
    public static string? Insert(
        string fullMarkdown,
        string markdown,
        string position,
        string? heading,
        int occurrence = 1)
    {
        var nl = fullMarkdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertBlock = (markdown ?? "").Replace("\r\n", "\n").TrimEnd() + "\n";
        if (nl == "\r\n")
            insertBlock = insertBlock.Replace("\n", "\r\n");

        SplitFm(fullMarkdown, out var fmBlock, out var body);
        var bodyNl = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var insertLines = insertBlock.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        int at;
        if (string.Equals(position, "top", StringComparison.OrdinalIgnoreCase))
        {
            at = 0;
            if (lines.Length > 0 && HeadingLine.IsMatch(lines[0].TrimEnd()))
                at = 1;
            // Skip a single blank line after H1 so insert sits under the title cleanly.
            if (at == 1 && lines.Length > 1 && string.IsNullOrWhiteSpace(lines[1]))
                at = 2;
        }
        else if (string.Equals(position, "afterHeading", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(heading))
                return null;
            var want = heading.Trim();
            var seen = 0;
            at = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                var m = HeadingLine.Match(lines[i].TrimEnd());
                if (!m.Success) continue;
                var text = m.Groups[2].Value.Trim();
                if (!text.Equals(want, StringComparison.Ordinal)) continue;
                seen++;
                if (seen == Math.Max(1, occurrence))
                {
                    at = i + 1;
                    break;
                }
            }
            if (at < 0) return null;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(position), "position must be top or afterHeading");
        }

        var next = new List<string>(lines.Length + insertLines.Length + 1);
        next.AddRange(lines.Take(at));
        // Ensure a blank line before insert when previous line has content.
        if (at > 0 && at <= lines.Length && !string.IsNullOrWhiteSpace(lines[at - 1]) &&
            insertLines.Length > 0 && !string.IsNullOrWhiteSpace(insertLines[0]))
            next.Add("");
        next.AddRange(insertLines);
        if (at < lines.Length && insertLines.Length > 0 &&
            !string.IsNullOrWhiteSpace(insertLines[^1]) &&
            !string.IsNullOrWhiteSpace(lines[at]))
            next.Add("");
        next.AddRange(lines.Skip(at));

        var newBody = string.Join(bodyNl == "\r\n" ? "\r\n" : "\n", next);
        if (fullMarkdown.EndsWith("\n", StringComparison.Ordinal) && !newBody.EndsWith("\n", StringComparison.Ordinal))
            newBody += bodyNl == "\r\n" ? "\r\n" : "\n";
        return fmBlock + newBody;
    }

    public static IReadOnlyList<object> FindSnippets(string bodyMarkdown, string query, int max = 3, int context = 160)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return [];
        var body = bodyMarkdown.Replace("\r\n", "\n");
        var lines = body.Split('\n');
        var results = new List<object>();
        string? currentHeading = null;

        // Walk lines for heading context + collect match windows in full body.
        var lower = body.ToLowerInvariant();
        var qLower = q.ToLowerInvariant();
        var searchFrom = 0;
        while (results.Count < max)
        {
            var idx = lower.IndexOf(qLower, searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;

            // Nearest heading: last heading line before this offset.
            var lineStart = body.LastIndexOf('\n', Math.Max(0, idx - 1)) + 1;
            var lineNo = CountLines(body, lineStart);
            currentHeading = NearestHeading(lines, lineNo);

            var start = Math.Max(0, idx - context / 2);
            var len = Math.Min(context, body.Length - start);
            var snippet = body.Substring(start, len).Replace('\n', ' ').Trim();
            if (start > 0) snippet = "…" + snippet;
            if (start + len < body.Length) snippet += "…";

            results.Add(new { text = snippet, heading = currentHeading, offset = idx });
            searchFrom = idx + Math.Max(1, q.Length);
        }
        return results;
    }

    private static string? NearestHeading(string[] lines, int lineIndex)
    {
        for (var i = Math.Min(lineIndex, lines.Length - 1); i >= 0; i--)
        {
            var m = HeadingLine.Match(lines[i].TrimEnd());
            if (m.Success) return m.Groups[2].Value.Trim();
        }
        return null;
    }

    private static int CountLines(string s, int endExclusive)
    {
        var n = 0;
        for (var i = 0; i < endExclusive && i < s.Length; i++)
            if (s[i] == '\n') n++;
        return n;
    }

    private static void SplitFm(string full, out string fmBlock, out string body)
    {
        var n = full.Replace("\r\n", "\n");
        if (!n.StartsWith("---\n", StringComparison.Ordinal))
        {
            fmBlock = "";
            body = full;
            return;
        }
        var end = n.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            fmBlock = "";
            body = full;
            return;
        }
        var fmLenInNormalized = end + "\n---\n".Length;
        // Map back to original length approximately by counting — prefer reconstructing from original.
        var orig = full;
        var match = Regex.Match(orig, @"\A---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline);
        if (match.Success)
        {
            fmBlock = match.Value;
            body = orig[match.Length..];
            return;
        }
        fmBlock = "";
        body = full;
        _ = fmLenInNormalized;
    }
}
