using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jotdex.Infrastructure.Search;

public sealed record ParsedSearchQuery
{
    public string FreeText { get; init; } = "";
    public string? Folder { get; init; }
    public string? Tag { get; init; }
    public string? Title { get; init; }
    public bool InCode { get; init; }
    public bool HasAttachment { get; init; }
    public int? ModifiedWithinDays { get; init; }
    public bool Literal { get; init; }
    public string Display { get; init; } = "";
}

public static partial class SearchQueryParser
{
    public static ParsedSearchQuery Parse(string raw, bool forceLiteral = false)
    {
        raw ??= "";
        var remaining = new StringBuilder();
        string? folder = null;
        string? tag = null;
        string? title = null;
        var inCode = false;
        var hasAttachment = false;
        int? modifiedDays = null;
        var literal = forceLiteral;

        var i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) break;

            if (raw[i] == '"')
            {
                var end = raw.IndexOf('"', i + 1);
                if (end < 0)
                {
                    remaining.Append(raw[i..]);
                    break;
                }

                var quoted = raw[(i + 1)..end];
                literal = true;
                if (remaining.Length > 0) remaining.Append(' ');
                remaining.Append(quoted);
                i = end + 1;
                continue;
            }

            var slice = raw[i..];
            var m = FilterRegex().Match(slice);
            if (m.Success && m.Index == 0)
            {
                var key = m.Groups[1].Value.ToLowerInvariant();
                var val = m.Groups[2].Success ? Unwrap(m.Groups[2].Value) : "";
                switch (key)
                {
                    case "folder":
                        folder = val;
                        break;
                    case "tag":
                        tag = val;
                        break;
                    case "title":
                        title = val;
                        break;
                    case "in" when val.Equals("code", StringComparison.OrdinalIgnoreCase):
                    case "type" when val.Equals("code", StringComparison.OrdinalIgnoreCase):
                        inCode = true;
                        break;
                    case "has" when val.Equals("attachment", StringComparison.OrdinalIgnoreCase):
                        hasAttachment = true;
                        break;
                    case "modified" when val.EndsWith('d') &&
                                         int.TryParse(val[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days):
                        modifiedDays = days;
                        break;
                    default:
                        // Malformed / unknown filter → ordinary text
                        if (remaining.Length > 0) remaining.Append(' ');
                        remaining.Append(m.Value);
                        break;
                }

                i += m.Length;
                continue;
            }

            var nextSpace = raw.IndexOfAny([' ', '\t'], i);
            if (nextSpace < 0) nextSpace = raw.Length;
            if (remaining.Length > 0) remaining.Append(' ');
            remaining.Append(raw[i..nextSpace]);
            i = nextSpace;
        }

        var free = remaining.ToString().Trim();
        return new ParsedSearchQuery
        {
            FreeText = free,
            Folder = folder,
            Tag = tag,
            Title = title,
            InCode = inCode,
            HasAttachment = hasAttachment,
            ModifiedWithinDays = modifiedDays,
            Literal = literal || forceLiteral,
            Display = raw.Trim()
        };
    }

    private static string Unwrap(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];
        return value;
    }

    [GeneratedRegex(@"^(folder|tag|title|in|type|has|modified):(""[^""]*""|[^\s]+)")]
    private static partial Regex FilterRegex();
}
