using System.Text.RegularExpressions;
using Jotdex.Core.Integrations;

namespace Jotdex.Core.Markdown;

/// <summary>
/// Exact save equivalence — shared with the TypeScript editor (documentSameness.ts).
/// LF line endings, trim end, ignore YAML <c>modified:</c> and server-owned provenance keys.
/// Interior blanks are significant.
/// </summary>
public static class DocumentSameness
{
    public static bool EqualsExactSave(string a, string b) =>
        string.Equals(NormalizeExactSave(a), NormalizeExactSave(b), StringComparison.Ordinal);

    public static string NormalizeExactSave(string content)
    {
        var n = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimEnd();
        if (n.StartsWith("---", StringComparison.Ordinal))
        {
            var end = n.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end > 0)
            {
                var header = n[3..end];
                foreach (var key in ProvenanceKeys.ServerOwned)
                {
                    header = Regex.Replace(
                        header,
                        $@"^{Regex.Escape(key)}:\s*.*$",
                        $"{key}:",
                        RegexOptions.Multiline | RegexOptions.IgnoreCase);
                }
                n = "---" + header + n[end..];
            }
        }
        return n;
    }
}
