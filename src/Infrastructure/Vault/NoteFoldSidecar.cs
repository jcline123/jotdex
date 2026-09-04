using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Vault;

/// <summary>
/// Sibling helper next to a note: <c>Title.md</c> + <c>Title.folds.json</c>.
/// Collapse state is not Markdown and must not go through note history/autosave.
/// </summary>
public static class NoteFoldSidecar
{
    public const string FileSuffix = ".folds.json";
    public const int MaxCollapsed = 400;
    public const int MaxKeyLength = 500;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Regex FoldKeyPattern = new(
        @"^[1-6]:[1-9]\d{0,3}:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string PathBesideMarkdown(string markdownAbsolutePath)
    {
        var dir = Path.GetDirectoryName(markdownAbsolutePath)
            ?? throw new ArgumentException("Note path has no directory.", nameof(markdownAbsolutePath));
        var stem = Path.GetFileNameWithoutExtension(markdownAbsolutePath);
        return Path.Combine(dir, stem + FileSuffix);
    }

    public static IReadOnlyList<string> ReadCollapsed(string markdownAbsolutePath)
    {
        var path = PathBesideMarkdown(markdownAbsolutePath);
        if (!File.Exists(path)) return [];
        try
        {
            var dto = JsonSerializer.Deserialize<HeadingFoldFile>(File.ReadAllText(path), JsonOpts);
            return Sanitize(dto?.Collapsed);
        }
        catch
        {
            return [];
        }
    }

    public static string Serialize(IReadOnlyList<string> collapsed)
    {
        var dto = new HeadingFoldFile { V = 1, Collapsed = Sanitize(collapsed).ToList() };
        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    public static IReadOnlyList<string> Sanitize(IEnumerable<string>? keys)
    {
        if (keys is null) return [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var raw in keys)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = raw.Trim();
            if (key.Length > MaxKeyLength) key = key[..MaxKeyLength];
            if (!FoldKeyPattern.IsMatch(key)) continue;
            if (!seen.Add(key)) continue;
            list.Add(key);
            if (list.Count >= MaxCollapsed) break;
        }
        return list;
    }

    public static void MoveBeside(string oldMarkdownAbsolute, string newMarkdownAbsolute)
    {
        var src = PathBesideMarkdown(oldMarkdownAbsolute);
        var dest = PathBesideMarkdown(newMarkdownAbsolute);
        if (!File.Exists(src)) return;
        if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(src, dest, overwrite: true);
    }

    public static void CopyBeside(string oldMarkdownAbsolute, string newMarkdownAbsolute)
    {
        var src = PathBesideMarkdown(oldMarkdownAbsolute);
        if (!File.Exists(src)) return;
        var dest = PathBesideMarkdown(newMarkdownAbsolute);
        if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: true);
    }

    public static void DeleteBeside(string markdownAbsolutePath)
    {
        var path = PathBesideMarkdown(markdownAbsolutePath);
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class HeadingFoldFile
    {
        public int V { get; set; } = 1;
        [JsonPropertyName("collapsed")]
        public List<string>? Collapsed { get; set; }
    }
}
