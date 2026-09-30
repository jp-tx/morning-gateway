using MorningGateway.Models;
using MorningGateway.Services.Calendar;

namespace MorningGateway.Tests;

[Collection("MauiStatics")]
public class CalendarStoreAndAggregatorTests : MauiStaticsTestBase
{
    static readonly DateTimeOffset S = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset E = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    // README "First launch: demo calendar"
    [Fact]
    public void Fresh_install_seeds_exactly_one_demo_ics_source()
    {
        var sources = new CalendarSourceStore().Load();
        var demo = Assert.Single(sources);
        Assert.Equal("Demo Calendar", demo.DisplayName);
        Assert.Equal(CalendarSourceType.IcsUrl, demo.Type);
        Assert.False(string.IsNullOrWhiteSpace(demo.IcsUrl));
    }

    [Fact]
    public async Task Removed_demo_calendar_does_not_come_back()
    {
        var store = new CalendarSourceStore();
        var demo = store.Load().Single();
        await store.RemoveAsync(demo.Id);
        Assert.Empty(new CalendarSourceStore().Load());
    }

    [Fact]
    public async Task Removing_a_source_deletes_its_stored_secret()
    {
        var store = new CalendarSourceStore();
        var src = new CalendarSourceConfig { DisplayName = "CD", Type = CalendarSourceType.CalDav };
        store.Upsert(src);
        await SecureStorage.Default.SetAsync(src.SecureStorageKey, "pw");
        await store.RemoveAsync(src.Id);
        Assert.Null(await SecureStorage.Default.GetAsync(src.SecureStorageKey));
    }

    [Fact]
    public void Upsert_updates_in_place_and_raises_SourcesChanged()
    {
        var store = new CalendarSourceStore();
        store.Load();                       // first load seeds the demo source
        var changes = 0;
        store.SourcesChanged += () => changes++;
        var src = new CalendarSourceConfig { DisplayName = "One", Type = CalendarSourceType.IcsUrl };
        store.Upsert(src);
        src.DisplayName = "Renamed";
        store.Upsert(src);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Count);            // demo + ours
        Assert.Equal("Renamed", loaded.Single(s => s.Id == src.Id).DisplayName);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Corrupt_saved_json_falls_back_to_empty()
    {
        Preferences.Default.Set("calendar_sources_json", "{not json");
        Assert.Empty(new CalendarSourceStore().Load());
    }

    // ---- aggregator ----

    class FakeProvider : ICalendarProvider
    {
        public CalendarSourceType Type { get; init; }
        public Func<CalendarSourceConfig, IReadOnlyList<CalendarEvent>> Events { get; init; } = _ => Array.Empty<CalendarEvent>();
        public int Calls;
        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarSourceConfig s, DateTimeOffset a, DateTimeOffset b, CancellationToken c = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Events(s));
        }
    }

    static CalendarEvent Ev(string id, string src, int day) => new()
    {
        Id = id, SourceId = src, Title = id,
        Start = new DateTimeOffset(2026, 10, day, 10, 0, 0, TimeSpan.Zero), End = new DateTimeOffset(2026, 10, day, 11, 0, 0, TimeSpan.Zero),
    };

    CalendarSourceStore StoreWith(params CalendarSourceConfig[] sources)
    {
        var store = new CalendarSourceStore();
        store.Save(sources.ToList());
        return store;
    }

    [Fact]
    public async Task Merges_sources_sorted_by_start_and_skips_disabled_ones()
    {
        var a = new CalendarSourceConfig { DisplayName = "a", Type = CalendarSourceType.IcsUrl };
        var b = new CalendarSourceConfig { DisplayName = "b", Type = CalendarSourceType.Google };
        var off = new CalendarSourceConfig { DisplayName = "off", Type = CalendarSourceType.CalDav, Enabled = false };
        var agg = new CalendarAggregatorService(StoreWith(a, b, off), new ICalendarProvider[]
        {
            new FakeProvider { Type = CalendarSourceType.IcsUrl, Events = s => new[] { Ev("late", s.Id, 9), Ev("early", s.Id, 2) } },
            new FakeProvider { Type = CalendarSourceType.Google, Events = s => new[] { Ev("mid", s.Id, 5) } },
            new FakeProvider { Type = CalendarSourceType.CalDav, Events = s => new[] { Ev("hidden", s.Id, 1) } },
        });

        var events = await agg.GetEventsAsync(S, E);
        Assert.Equal(new[] { "early", "mid", "late" }, events.Select(e => e.Title));
    }

    [Fact]
    public async Task One_failing_calendar_does_not_blank_the_others()
    {
        var good = new CalendarSourceConfig { DisplayName = "good", Type = CalendarSourceType.IcsUrl };
        var bad = new CalendarSourceConfig { DisplayName = "bad", Type = CalendarSourceType.Google };
        var agg = new CalendarAggregatorService(StoreWith(good, bad), new ICalendarProvider[]
        {
            new FakeProvider { Type = CalendarSourceType.IcsUrl, Events = s => new[] { Ev("ok", s.Id, 3) } },
            new FakeProvider { Type = CalendarSourceType.Google, Events = _ => throw new HttpRequestException("offline") },
        });

        Assert.Equal("ok", Assert.Single(await agg.GetEventsAsync(S, E)).Title);
    }

    [Fact]
    public async Task Results_are_cached_for_a_covered_range_until_forced()
    {
        var src = new CalendarSourceConfig { DisplayName = "a", Type = CalendarSourceType.IcsUrl };
        var provider = new FakeProvider { Type = CalendarSourceType.IcsUrl, Events = s => new[] { Ev("x", s.Id, 3) } };
        var agg = new CalendarAggregatorService(StoreWith(src), new[] { provider });

        await agg.GetEventsAsync(S, E);
        await agg.GetEventsAsync(S.AddDays(3), E.AddDays(-3));
        Assert.Equal(1, provider.Calls);

        await agg.GetEventsAsync(S, E, forceRefresh: true);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Revoked_google_access_is_reported_as_a_problem_and_clears_on_recovery()
    {
        var g = new CalendarSourceConfig { DisplayName = "me@example.com", Type = CalendarSourceType.Google };
        var fail = true;
        var agg = new CalendarAggregatorService(StoreWith(g), new ICalendarProvider[]
        {
            new FakeProvider { Type = CalendarSourceType.Google, Events = s => fail ? throw new GoogleReauthRequiredException() : new[] { Ev("ok", s.Id, 3) } },
        });

        Assert.Empty(await agg.GetEventsAsync(S, E));
        Assert.Equal("me@example.com: reconnect in Settings", Assert.Single(agg.Problems));

        fail = false;
        Assert.Single(await agg.GetEventsAsync(S, E, forceRefresh: true));
        Assert.Empty(agg.Problems);
    }

    [Fact]
    public async Task Ordinary_failures_such_as_being_offline_are_not_reported_as_problems()
    {
        var g = new CalendarSourceConfig { DisplayName = "x", Type = CalendarSourceType.IcsUrl };
        var agg = new CalendarAggregatorService(StoreWith(g), new ICalendarProvider[]
        {
            new FakeProvider { Type = CalendarSourceType.IcsUrl, Events = _ => throw new HttpRequestException("offline") },
        });
        await agg.GetEventsAsync(S, E);
        Assert.Empty(agg.Problems);
    }
}
