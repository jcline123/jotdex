using System.Text.RegularExpressions;
using Jotdex.Core.Snippets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Infrastructure.Snippets;

public static partial class SnippetNoteParser
{
    public static bool IsSnippet(IReadOnlyDictionary<string, string?> fields) =>
        fields.TryGetValue(SnippetConstants.TypeKey, out var t) &&
        string.Equals(t?.Trim(), SnippetConstants.TypeValue, StringComparison.OrdinalIgnoreCase);

    public static SnippetSummary? FromNote(NoteDetail note)
    {
        if (!IsSnippet(note.FrontMatter))
            return null;

        note.FrontMatter.TryGetValue(SnippetConstants.TriggerKey, out var trigger);
        note.FrontMatter.TryGetValue(SnippetConstants.LanguageKey, out var language);
        trigger = (trigger ?? "").Trim();
        language = (language ?? "plaintext").Trim();
        if (string.IsNullOrEmpty(trigger))
            trigger = Slugify(note.Title);

        var parsed = FrontMatterParser.Parse(note.Markdown);
        var (code, langFromFence) = ExtractFirstCodeBlock(parsed.Body);
        if (string.IsNullOrEmpty(language) || language == "plaintext")
            language = langFromFence ?? "plaintext";

        var description = ExtractDescription(parsed.Body, note.Title);
        return new SnippetSummary(
            note.Id,
            note.Title,
            trigger,
            language,
            note.FolderPath,
            note.RelativePath,
            description,
            code,
            note.Tags);
    }

    private static (string Code, string? Language) ExtractFirstCodeBlock(string body)
    {
        var m = FencedCodeRegex().Match(body);
        if (!m.Success)
            return ("", null);
        var lang = m.Groups[1].Value.Trim();
        var code = m.Groups[2].Value;
        if (code.EndsWith('\n'))
            code = code[..^1];
        return (code, string.IsNullOrWhiteSpace(lang) ? null : lang);
    }

    private static string? ExtractDescription(string body, string title)
    {
        var lines = body.Split('\n');
        var inBlock = false;
        var desc = new List<string>();
        foreach (var line in lines)
        {
            if (FencedCodeOpenRegex().IsMatch(line))
            {
                inBlock = true;
                continue;
            }
            if (inBlock)
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    break;
                continue;
            }
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('#'))
            {
                var h = t.TrimStart('#').Trim();
                if (string.Equals(h, title, StringComparison.OrdinalIgnoreCase)) continue;
            }
            desc.Add(t);
            if (desc.Count >= 2) break;
        }
        var text = string.Join(' ', desc).Trim();
        return text.Length > 0 ? text : null;
    }

    public static string Slugify(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        s = NonSlugRegex().Replace(s, "-");
        s = s.Trim('-');
        return string.IsNullOrEmpty(s) ? "snippet" : s;
    }

    [GeneratedRegex("^```(\\w*)\\s*\\n([\\s\\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex("^```")]
    private static partial Regex FencedCodeOpenRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugRegex();
}
