using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Export;

/// <summary>
/// Strips product fingerprints from Share / static note HTML. On-disk Markdown is unchanged.
/// </summary>
public static partial class ShareHtmlAnonymizer
{
    public static string StripProductIdentifiers(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        html = ProductCommentRegex().Replace(html, "");
        html = ProductDataAttrRegex().Replace(html, "");
        html = ProductPrefixRegex().Replace(html, "");
        return html;
    }

    [GeneratedRegex(@"<!--\s*/?jotdex[\s\S]*?-->", RegexOptions.IgnoreCase)]
    private static partial Regex ProductCommentRegex();

    [GeneratedRegex(@"\s+data-jotdex(?:-[a-z0-9]+)*(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex ProductDataAttrRegex();

    [GeneratedRegex(@"\bjotdex-", RegexOptions.IgnoreCase)]
    private static partial Regex ProductPrefixRegex();
}
