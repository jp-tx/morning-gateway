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

    [Theory]
    [InlineData(0.75, 4)]
    [InlineData(1.0, 3)]
    [InlineData(1.4, 2)]
    [InlineData(1.75, 1)]
    public void Larger_ui_scales_show_fewer_event_chips_per_day(double scale, int expected) =>
        Assert.Equal(expected, MonthDayCell.MaxVisibleForScale(scale));

    [Fact]
    public void Overflow_count_follows_the_visible_limit()
    {
        var events = Enumerable.Range(0, 4).Select(_ => At(new DateTime(2026, 10, 2, 9, 0, 0), new DateTime(2026, 10, 2, 10, 0, 0))).ToList();
        var cell = new MonthDayCell { Date = new DateOnly(2026, 10, 2), IsCurrentMonth = true, IsToday = false, Events = events, MaxVisible = 1 };
        Assert.Single(cell.VisibleEvents);
        Assert.Equal("+3 more", cell.OverflowText);
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

[Collection("MauiStatics")]
public class UiScaleTests : MauiStaticsTestBase
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.1, 0.75)]
    [InlineData(9.0, 1.75)]
    [InlineData(1.234, 1.23)]
    [InlineData(double.NaN, 1.0)]
    public void Scale_is_clamped_to_a_usable_range(double input, double expected) =>
        Assert.Equal(expected, MorningGateway.Services.Display.UiScale.Clamp(input));

    [Theory]
    [InlineData(320, 1.0, 320)]
    [InlineData(320, 1.5, 480)]
    [InlineData(320, 0.75, 240)]
    [InlineData(280, 9.0, 490)]   // clamped to 1.75
    public void Density_scales_with_the_setting(int deviceDpi, double scale, int expected) =>
        Assert.Equal(expected, MorningGateway.Services.Display.UiScale.ScaledDensityDpi(deviceDpi, scale));

    [Fact]
    public void Setting_defaults_to_100_percent_persists_and_clamps()
    {
        var store = new MorningGateway.Services.Display.SettingsStore();
        Assert.Equal(1.0, store.UiScale);
        store.UiScale = 1.5;
        Assert.Equal(1.5, new MorningGateway.Services.Display.SettingsStore().UiScale);
        store.UiScale = 50;
        Assert.Equal(1.75, store.UiScale);
    }
}
