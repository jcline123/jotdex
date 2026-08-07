using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Vault;

public sealed class FrontMatterResult
{
    public required IReadOnlyDictionary<string, string?> Fields { get; init; }
    public required string Body { get; init; }
}

public static partial class FrontMatterParser
{
    public static FrontMatterResult Parse(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return new FrontMatterResult
            {
                Fields = new Dictionary<string, string?>(),
                Body = content
            };
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
        {
            return new FrontMatterResult
            {
                Fields = new Dictionary<string, string?>(),
                Body = content
            };
        }

        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var i = 1;
        string? listKey = null;
        var listValues = new List<string>();

        void FlushList()
        {
            if (listKey is null) return;
            fields[listKey] = string.Join('\n', listValues);
            listKey = null;
            listValues.Clear();
        }

        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---")
            {
                FlushList();
                i++;
                break;
            }

            if (listKey is not null && ListItemRegex().IsMatch(line))
            {
                var item = ListItemRegex().Match(line).Groups[1].Value.Trim().Trim('"', '\'');
                listValues.Add(item);
                continue;
            }

            FlushList();

            var m = KeyValueRegex().Match(line);
            if (!m.Success) continue;

            var key = m.Groups[1].Value.Trim();
            var value = m.Groups[2].Value.Trim();
            if (value.Length == 0)
            {
                listKey = key;
                continue;
            }

            fields[key] = Unwrap(value);
        }

        FlushList();
        var body = string.Join('\n', lines.Skip(i)).TrimStart('\n');
        return new FrontMatterResult { Fields = fields, Body = body };
    }

    public static string? DeriveTitle(IReadOnlyDictionary<string, string?> fields, string body, string fileNameWithoutExt)
    {
        if (fields.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
            return title;

        var h1 = HeadingRegex().Match(body);
        if (h1.Success) return h1.Groups[1].Value.Trim();

        return fileNameWithoutExt;
    }

    public static Guid DeriveId(IReadOnlyDictionary<string, string?> fields, string relativePath)
    {
        if (fields.TryGetValue("id", out var idText) && Guid.TryParse(idText, out var id))
            return id;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath.Replace('\\', '/')));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    public static IReadOnlyList<string> ParseTags(IReadOnlyDictionary<string, string?> fields)
    {
        if (!fields.TryGetValue("tags", out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];

        if (raw.Contains('\n'))
            return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // [a, b] or a, b
        raw = raw.Trim().TrimStart('[').TrimEnd(']');
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\''))
            .Where(t => t.Length > 0)
            .ToArray();
    }

    public static DateTimeOffset? ParseDate(IReadOnlyDictionary<string, string?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTimeOffset.TryParse(raw, out var dt) ? dt : null;
    }

    private static string Unwrap(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }

    [GeneratedRegex(@"^([A-Za-z0-9_\-]+)\s*:\s*(.*)$")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"^\s*-\s+(.+)$")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();
}
