using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jotdex.Infrastructure.Calendar;

/// <summary>
/// Decides which day a todo occupies and builds the all-day event sent to CalDAV.
/// Undated and overdue open todos stay on today so they roll forward. A future due date stays on that day.
/// </summary>
public static class CalendarPlanner
{
    public static DateOnly TargetDay(string? due, DateOnly today)
    {
        if (TryParseDue(due, out var day) && day > today)
            return day;
        return today;
    }

    public static bool TryParseDue(string? due, out DateOnly day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(due)) return false;
        if (DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            day = DateOnly.FromDateTime(dto.ToLocalTime().DateTime);
            return true;
        }
        return DateOnly.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    public static string ObjectFileName(string taskId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(taskId ?? ""))).ToLowerInvariant();
        return "jotdex-" + hash[..32] + ".ics";
    }

    public static string EventUid(string taskId) =>
        ObjectFileName(taskId)[..^4] + "@jotdex";

    public const string SummaryPrefix = "todo: ";

    public static string EventHref(string calendarUrl, string taskId)
    {
        var root = (calendarUrl ?? "").Trim();
        if (!root.EndsWith('/')) root += "/";
        return root + ObjectFileName(taskId);
    }

    public static string FormatSummary(string text) => SummaryPrefix + (text ?? "").Trim();

    public static bool IsJotdexUid(string? uid) =>
        !string.IsNullOrWhiteSpace(uid) && uid.Trim().EndsWith("@jotdex", StringComparison.OrdinalIgnoreCase);

    /// <summary>All-day events titled "todo: …" that were created outside Jotdex.</summary>
    public static bool TryParseImportSummary(string? summary, out string title)
    {
        title = "";
        if (string.IsNullOrWhiteSpace(summary)) return false;
        var text = summary.Trim();
        if (!text.StartsWith("todo:", StringComparison.OrdinalIgnoreCase)) return false;
        title = text[5..].Trim();
        return title.Length > 0;
    }

    public static bool TryNormalizePublicBase(string? raw, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var text = raw.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "Jotdex address must be an https site, such as jotdex.example.com.";
            return false;
        }
        var path = uri.AbsolutePath.TrimEnd('/');
        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (path.Length > 0)
            normalized += path;
        return true;
    }

    public static string? NoteLink(string? publicBaseUrl, Guid noteId)
    {
        if (!TryNormalizePublicBase(publicBaseUrl, out var baseUrl, out _) || string.IsNullOrEmpty(baseUrl))
            return null;
        if (noteId == Guid.Empty) return null;
        return baseUrl + "/?note=" + noteId.ToString("D");
    }

    public static string BuildIcs(string taskId, string summary, DateOnly day, string? noteUrl, string? uidOverride = null)
    {
        var end = day.AddDays(1);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var description = string.IsNullOrWhiteSpace(noteUrl)
            ? "Jotdex todo"
            : "Open in Jotdex: " + noteUrl;
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Jotdex//Jotdex//EN",
            "CALSCALE:GREGORIAN",
            "BEGIN:VEVENT",
            "UID:" + (string.IsNullOrWhiteSpace(uidOverride) ? EventUid(taskId) : uidOverride.Trim()),
            "DTSTAMP:" + stamp,
            "DTSTART;VALUE=DATE:" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "DTEND;VALUE=DATE:" + end.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "SUMMARY:" + EscapeText(FormatSummary(summary)),
            "DESCRIPTION:" + EscapeText(description)
        };
        if (!string.IsNullOrWhiteSpace(noteUrl))
            lines.Add("URL:" + noteUrl.Trim());
        lines.Add("STATUS:CONFIRMED");
        lines.Add("TRANSP:TRANSPARENT");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines.Select(Fold)) + "\r\n";
    }

    public static IReadOnlyList<ParsedCalendarEvent> ParseEvents(string? ics)
    {
        var found = new List<ParsedCalendarEvent>();
        if (string.IsNullOrWhiteSpace(ics)) return found;
        var logical = string.Join("\n", Unfold(ics));
        var blocks = logical.Split("BEGIN:VEVENT", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var end = block.IndexOf("END:VEVENT", StringComparison.OrdinalIgnoreCase);
            if (end < 0) continue;
            string? uid = null;
            string? summary = null;
            DateOnly? start = null;
            var allDay = false;
            foreach (var line in block[..end].Split('\n'))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon];
                var value = UnescapeText(line[(colon + 1)..].Trim());
                var prop = name.Split(';')[0];
                if (prop.Equals("UID", StringComparison.OrdinalIgnoreCase))
                    uid = value;
                else if (prop.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) && summary is null)
                    summary = value;
                else if (prop.Equals("DTSTART", StringComparison.OrdinalIgnoreCase) && start is null)
                {
                    allDay = name.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) ||
                             (value.Length == 8 && value.All(char.IsDigit));
                    if (value.Length >= 8 && DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                        start = day;
                }
            }
            if (string.IsNullOrWhiteSpace(uid)) continue;
            found.Add(new ParsedCalendarEvent
            {
                Uid = uid,
                Summary = summary ?? "",
                Start = start,
                AllDay = allDay
            });
        }
        return found;
    }

    public static string UnescapeText(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch
                {
                    'n' or 'N' => '\n',
                    _ => next
                });
                continue;
            }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static List<string> Unfold(string ics)
    {
        var text = ics.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var logical = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            if (logical.Count > 0 && (raw.StartsWith(' ') || raw.StartsWith('\t')))
                logical[^1] += raw[1..];
            else
                logical.Add(raw);
        }
        return logical;
    }

    public static string EscapeText(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';': sb.Append("\\;"); break;
                case ',': sb.Append("\\,"); break;
                case '\n': sb.Append("\\n"); break;
                default:
                    if (ch >= 32) sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>RFC 5545 line folding at 75 octets.</summary>
    public static string Fold(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length <= 75) return line;
        var sb = new StringBuilder();
        var index = 0;
        var limit = 75;
        while (index < line.Length)
        {
            var take = 0;
            var used = 0;
            while (index + take < line.Length)
            {
                var next = Encoding.UTF8.GetByteCount(line.AsSpan(index + take, 1));
                if (used + next > limit) break;
                used += next;
                take++;
            }
            if (take == 0) take = 1;
            if (sb.Length > 0) sb.Append("\r\n ");
            sb.Append(line, index, take);
            index += take;
            limit = 74;
        }
        return sb.ToString();
    }
}

public sealed class ParsedCalendarEvent
{
    public required string Uid { get; init; }
    public required string Summary { get; init; }
    public DateOnly? Start { get; init; }
    public bool AllDay { get; init; }
}
