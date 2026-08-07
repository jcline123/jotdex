using System.Text;

namespace Jotdex.Infrastructure.Vault;

/// <summary>Rewrites Markdown relative links when a note stem or folder path changes.</summary>
public static class MarkdownLinkRewriter
{
    /// <summary>Rewrite sibling .assets folder references after a note file rename.</summary>
    public static string RewriteAssetStem(string markdown, string oldStem, string newStem)
    {
        if (string.IsNullOrEmpty(markdown) ||
            string.IsNullOrEmpty(oldStem) ||
            string.Equals(oldStem, newStem, StringComparison.Ordinal))
            return markdown;

        var result = markdown;
        var replacements = new (string Old, string New)[]
        {
            (oldStem + ".assets", newStem + ".assets"),
            (EncodeSpaces(oldStem) + ".assets", EncodeSpaces(newStem) + ".assets"),
            (Uri.EscapeDataString(oldStem) + ".assets", Uri.EscapeDataString(newStem) + ".assets"),
        };

        foreach (var (oldForm, newForm) in replacements.DistinctBy(x => x.Old, StringComparer.Ordinal))
        {
            if (string.Equals(oldForm, newForm, StringComparison.Ordinal)) continue;
            result = ReplaceLinkTarget(result, oldForm, newForm);
        }

        return result;
    }

    /// <summary>Rewrite relative paths that mention an old folder prefix (folder rename/move).</summary>
    public static string RewriteFolderPrefix(string markdown, string oldFolderRel, string newFolderRel)
    {
        oldFolderRel = Normalize(oldFolderRel);
        newFolderRel = Normalize(newFolderRel);
        if (string.IsNullOrEmpty(oldFolderRel) ||
            string.Equals(oldFolderRel, newFolderRel, StringComparison.OrdinalIgnoreCase))
            return markdown;

        var result = markdown;
        var pairs = new (string Old, string New)[]
        {
            (oldFolderRel, newFolderRel),
            (EncodeSpaces(oldFolderRel), EncodeSpaces(newFolderRel)),
        };

        foreach (var (oldForm, newForm) in pairs.DistinctBy(x => x.Old, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(oldForm, newForm, StringComparison.OrdinalIgnoreCase)) continue;
            result = ReplaceLinkTarget(result, oldForm, newForm);
        }

        return result;
    }

    private static string ReplaceLinkTarget(string markdown, string oldTarget, string newTarget)
    {
        // Common Markdown / HTML-ish target endings after the path segment
        var sb = new StringBuilder(markdown.Length);
        var remaining = markdown.AsSpan();
        while (true)
        {
            var idx = remaining.IndexOf(oldTarget, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                sb.Append(remaining);
                break;
            }

            sb.Append(remaining[..idx]);
            sb.Append(newTarget);
            remaining = remaining[(idx + oldTarget.Length)..];
        }

        return sb.ToString();
    }

    private static string EncodeSpaces(string value) => value.Replace(" ", "%20", StringComparison.Ordinal);

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
