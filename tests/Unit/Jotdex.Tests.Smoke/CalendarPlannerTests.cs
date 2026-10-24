using Jotdex.Infrastructure.Calendar;

namespace Jotdex.Tests.Smoke;

public class CalendarPlannerTests
{
    [Fact]
    public void Undated_and_past_todos_land_on_today()
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Equal(today, CalendarPlanner.TargetDay(null, today));
        Assert.Equal(today, CalendarPlanner.TargetDay("", today));
        Assert.Equal(today, CalendarPlanner.TargetDay("2026-10-23", today));
        Assert.Equal(today, CalendarPlanner.TargetDay("2026-10-24", today));
    }

    [Fact]
    public void Future_due_date_stays_on_that_day()
    {
        var today = new DateOnly(2026, 10, 24);
        Assert.Equal(new DateOnly(2026, 11, 2), CalendarPlanner.TargetDay("2026-11-02", today));
    }

    [Fact]
    public void Ics_is_an_all_day_event_and_escapes_text()
    {
        var ics = CalendarPlanner.BuildIcs(
            "note:1",
            "Buy milk, eggs; review",
            new DateOnly(2026, 10, 24),
            "https://jotdex.example.com/?note=11111111-1111-1111-1111-111111111111");
        Assert.Contains("DTSTART;VALUE=DATE:20261024", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261025", ics);
        Assert.Contains("SUMMARY:todo: Buy milk\\, eggs\\; review", ics);
        Assert.Contains("URL:https://jotdex.example.com/?note=11111111-1111-1111-1111-111111111111", ics);
        Assert.DoesNotContain("BEGIN:VTODO", ics);
    }

    [Fact]
    public void Note_link_uses_the_public_address_and_phone_prefix_round_trips()
    {
        Assert.True(CalendarPlanner.TryNormalizePublicBase("jotdex.cline.xyz", out var normalized, out _));
        Assert.Equal("https://jotdex.cline.xyz", normalized);
        var noteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal(
            "https://jotdex.cline.xyz/?note=11111111-1111-1111-1111-111111111111",
            CalendarPlanner.NoteLink(normalized, noteId));

        Assert.True(CalendarPlanner.TryParseImportSummary("Todo: Call the vendor", out var title));
        Assert.Equal("Call the vendor", title);
        Assert.False(CalendarPlanner.TryParseImportSummary("Standup", out _));
        Assert.Equal("todo: Call the vendor", CalendarPlanner.FormatSummary(title));
    }

    [Fact]
    public void Phone_all_day_event_parses_without_treating_jotdex_events_as_imports()
    {
        var ics = """
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            UID:phone-1
            DTSTART;VALUE=DATE:20261024
            SUMMARY:todo: Pick up parts
            END:VEVENT
            BEGIN:VEVENT
            UID:jotdex-abc@jotdex
            DTSTART;VALUE=DATE:20261024
            SUMMARY:todo: Already synced
            END:VEVENT
            END:VCALENDAR
            """;
        var events = CalendarPlanner.ParseEvents(ics);
        Assert.Equal(2, events.Count);
        Assert.True(events[0].AllDay);
        Assert.Equal(new DateOnly(2026, 10, 24), events[0].Start);
        Assert.True(CalendarPlanner.TryParseImportSummary(events[0].Summary, out var title));
        Assert.Equal("Pick up parts", title);
        Assert.False(CalendarPlanner.IsJotdexUid(events[0].Uid));
        Assert.True(CalendarPlanner.IsJotdexUid(events[1].Uid));
    }

    [Fact]
    public void Event_file_name_is_stable_and_safe()
    {
        var name = CalendarPlanner.ObjectFileName("abc:def/1");
        Assert.StartsWith("jotdex-", name);
        Assert.EndsWith(".ics", name);
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("/", name);
        Assert.Equal(name, CalendarPlanner.ObjectFileName("abc:def/1"));
        Assert.NotEqual(name, CalendarPlanner.ObjectFileName("other"));
        Assert.EndsWith("/" + name, CalendarPlanner.EventHref("https://cal.example/calendars/personal", "abc:def/1"));
    }
}
