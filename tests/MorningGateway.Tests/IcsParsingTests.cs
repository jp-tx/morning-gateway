using MorningGateway.Services.Calendar;

namespace MorningGateway.Tests;

// README: "Plain ICS/webcal URL ... any .ics feed" and "recurring events" (the demo feed is daily-recurring).
public class IcsParsingTests
{
    static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\n" + string.Join("", events) + "END:VCALENDAR\r\n";

    static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset End = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_timed_event_is_parsed()
    {
        var ics = Ics("BEGIN:VEVENT\r\nUID:a1\r\nDTSTART:20261002T140000Z\r\nDTEND:20261002T150000Z\r\nSUMMARY:Dentist\r\nLOCATION:Main St\r\nEND:VEVENT\r\n");
        var events = IcsParsing.ParseEvents(ics, "src", "#123456", Start, End);

        var e = Assert.Single(events);
        Assert.Equal("Dentist", e.Title);
        Assert.Equal("Main St", e.Location);
        Assert.Equal("src", e.SourceId);
        Assert.Equal("#123456", e.ColorHex);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 14, 0, 0, TimeSpan.Zero), e.Start.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(1), e.End - e.Start);
        Assert.False(e.IsAllDay);
    }

    [Fact]
    public void Daily_recurring_event_expands_once_per_day_in_window()
    {
        var ics = Ics("BEGIN:VEVENT\r\nUID:standup\r\nDTSTART:20260920T090000Z\r\nDTEND:20260920T091500Z\r\nRRULE:FREQ=DAILY\r\nSUMMARY:Stand-up\r\nEND:VEVENT\r\n");
        var events = IcsParsing.ParseEvents(ics, "src", "#000", Start, End);

        Assert.Equal(7, events.Count);
        Assert.Equal(7, events.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Events_outside_the_window_are_excluded()
    {
        var ics = Ics("BEGIN:VEVENT\r\nUID:old\r\nDTSTART:20250101T100000Z\r\nDTEND:20250101T110000Z\r\nSUMMARY:Old\r\nEND:VEVENT\r\n");
        Assert.Empty(IcsParsing.ParseEvents(ics, "src", "#000", Start, End));
    }

    [Fact]
    public void Missing_summary_becomes_untitled()
    {
        var ics = Ics("BEGIN:VEVENT\r\nUID:x\r\nDTSTART:20261002T100000Z\r\nDTEND:20261002T110000Z\r\nEND:VEVENT\r\n");
        Assert.Equal("(untitled)", Assert.Single(IcsParsing.ParseEvents(ics, "src", "#000", Start, End)).Title);
    }

    [Fact]
    public void All_day_event_is_flagged()
    {
        var ics = Ics("BEGIN:VEVENT\r\nUID:h\r\nDTSTART;VALUE=DATE:20261003\r\nDTEND;VALUE=DATE:20261004\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n");
        Assert.True(Assert.Single(IcsParsing.ParseEvents(ics, "src", "#000", Start, End)).IsAllDay);
    }

    [Fact]
    public void All_day_event_lands_on_its_own_calendar_day_only()
    {
        // Run under TZ=America/New_York (and UTC) - a date-only event must not slide to the neighbouring day.
        var ics = Ics("BEGIN:VEVENT\r\nUID:h\r\nDTSTART;VALUE=DATE:20261003\r\nDTEND;VALUE=DATE:20261004\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n");
        var e = Assert.Single(IcsParsing.ParseEvents(ics, "src", "#000", Start, End));

        Assert.True(e.OccursOn(new DateOnly(2026, 10, 3)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 2)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public void Garbage_input_yields_no_events_instead_of_throwing()
    {
        Assert.Empty(IcsParsing.ParseEvents("<html>not a calendar</html>", "src", "#000", Start, End));
    }
}
