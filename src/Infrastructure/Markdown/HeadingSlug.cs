using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Markdown;

/// <summary>
/// Heading fragment ids aligned with <c>slugifyHeading</c> in the SPA
/// (<c>src/Web/src/editor/outline/liveOutline.ts</c>). Keeps leading digits so
/// headings like "3C Service…" become <c>3c-service…</c>, not Markdig Default's
/// digit-stripped <c>c-service…</c>.
/// </summary>
internal static partial class HeadingSlug
{
    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            // Match JS \w for ASCII: letter, digit, underscore; also allow whitespace and hyphen for later step.
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or ' ' or '\t')
                sb.Append(ch);
            // drop other punctuation (same idea as /[^\w\s-]/g)
        }
        var cleaned = sb.ToString();
        cleaned = Whitespace().Replace(cleaned, "-");
        cleaned = MultiHyphen().Replace(cleaned, "-");
        return cleaned.Trim('-');
    }

    public static string Unique(string baseSlug, Dictionary<string, int> used)
    {
        var slug = string.IsNullOrEmpty(baseSlug) ? "section" : baseSlug;
        var n = used.GetValueOrDefault(slug);
        used[slug] = n + 1;
        return n == 0 ? slug : $"{slug}-{n + 1}";
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"-+", RegexOptions.Compiled)]
    private static partial Regex MultiHyphen();
}
