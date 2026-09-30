using MorningGateway.Models;

namespace MorningGateway.Tests;

public class ModelTests
{
    static CalendarEvent At(DateTime localStart, DateTime localEnd) => new()
    {
        Id = "i", SourceId = "s", Title = "t",
        Start = new DateTimeOffset(localStart, TimeZoneInfo.Local.GetUtcOffset(localStart)),
        End = new DateTimeOffset(localEnd, TimeZoneInfo.Local.GetUtcOffset(localEnd)),
    };

    [Fact]
    public void Event_occurs_on_each_day_it_overlaps_and_not_the_next()
    {
        var e = At(new DateTime(2026, 10, 2, 22, 0, 0), new DateTime(2026, 10, 3, 1, 0, 0));
        Assert.True(e.OccursOn(new DateOnly(2026, 10, 2)));
        Assert.True(e.OccursOn(new DateOnly(2026, 10, 3)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 4)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Event_ending_exactly_at_midnight_does_not_spill_into_the_next_day()
    {
        var e = At(new DateTime(2026, 10, 2, 22, 0, 0), new DateTime(2026, 10, 3, 0, 0, 0));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void Month_cell_shows_three_events_and_an_overflow_count()
    {
        var events = Enumerable.Range(0, 5).Select(_ => At(new DateTime(2026, 10, 2, 9, 0, 0), new DateTime(2026, 10, 2, 10, 0, 0))).ToList();
        var cell = new MonthDayCell { Date = new DateOnly(2026, 10, 2), IsCurrentMonth = true, IsToday = false, Events = events };

        Assert.Equal(3, cell.VisibleEvents.Count());
        Assert.True(cell.HasOverflow);
        Assert.Equal("+2 more", cell.OverflowText);
        Assert.Equal("2", cell.DayNumberText);
    }

    [Fact]
    public void Month_cell_with_three_or_fewer_events_has_no_overflow()
    {
        var cell = new MonthDayCell { Date = new DateOnly(2026, 10, 2), IsCurrentMonth = true, IsToday = false, Events = new() };
        Assert.False(cell.HasOverflow);
    }

    [Fact]
    public void Source_secret_key_is_derived_from_id_and_never_a_plain_field()
    {
        var s = new CalendarSourceConfig { DisplayName = "x", Type = CalendarSourceType.CalDav };
        Assert.Equal($"calsource_secret_{s.Id}", s.SecureStorageKey);
        Assert.DoesNotContain(typeof(CalendarSourceConfig).GetProperties(), p => p.Name.Contains("Password") || p.Name.Contains("Token"));
    }
}
