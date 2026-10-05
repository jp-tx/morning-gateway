using MorningGateway.Models;
using MorningGateway.Services.Net;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Fans a date-range query out across every enabled calendar source (ICS,
/// Google, CalDAV), merges the results, and caches them briefly so switching
/// between month/day view doesn't re-hit the network every time. Each source's
/// last good result is also saved to disk, so when the network is down (or the
/// app was just restarted offline) the dashboard shows saved events instead of nothing.
/// </summary>
public class CalendarAggregatorService
{
    /// <summary>After a source fails, skip the network for it this long so navigating while offline stays instant.</summary>
    static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(45);

    readonly CalendarSourceStore _store;
    readonly IReadOnlyDictionary<CalendarSourceType, ICalendarProvider> _providers;
    readonly JsonFileCache? _disk;
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _failedAt = new();

    EventCache? _cache;

    /// <summary>Per-source problems from the most recent fetch (e.g. "me@x.com: reconnect the account in Settings"); empty when all is well.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    /// <summary>True when the latest result includes saved (not freshly fetched) events because a source couldn't be reached.</summary>
    public bool IsOffline { get; private set; }

    /// <summary>When the saved events shown while <see cref="IsOffline"/> were last fetched successfully (oldest across sources).</summary>
    public DateTimeOffset? StaleSince { get; private set; }

    public CalendarAggregatorService(CalendarSourceStore store, IEnumerable<ICalendarProvider> providers, JsonFileCache? diskCache = null)
    {
        _store = store;
        _providers = providers.ToDictionary(p => p.Type);
        _disk = diskCache;
    }

    /// <summary>Saved events only, no network - for painting the dashboard immediately at startup.</summary>
    public IReadOnlyList<CalendarEvent> GetSavedEvents(DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var merged = new List<CalendarEvent>();
        foreach (var source in _store.Load().Where(s => s.Enabled))
        {
            if (LoadSaved(source) is { } saved)
            {
                merged.AddRange(Overlapping(saved.Events, rangeStart, rangeEnd));
            }
        }

        return merged.OrderBy(e => e.Start).ToList();
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cache is { } cache && cache.Covers(rangeStart, rangeEnd) && cache.Age < TimeSpan.FromMinutes(5))
        {
            return cache.Events;
        }

        var sources = _store.Load().Where(s => s.Enabled).ToList();
        var problems = new System.Collections.Concurrent.ConcurrentBag<string>();
        var stale = new System.Collections.Concurrent.ConcurrentBag<DateTimeOffset>();
        var tasks = sources.Select(async source =>
        {
            if (!_providers.TryGetValue(source.Type, out var provider))
            {
                return Array.Empty<CalendarEvent>();
            }

            var recentlyFailed = !forceRefresh && _failedAt.TryGetValue(source.Id, out var at) && DateTimeOffset.UtcNow - at < FailureBackoff;
            if (!recentlyFailed)
            {
                try
                {
                    var fresh = await provider.GetEventsAsync(source, rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
                    _failedAt.TryRemove(source.Id, out _);
                    SaveFresh(source, fresh, rangeStart, rangeEnd);
                    return (IReadOnlyList<CalendarEvent>)fresh;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // One failing calendar (bad credentials, offline, etc.) shouldn't blank the whole dashboard.
                    if (ex is GoogleReauthRequiredException)
                    {
                        problems.Add($"{source.DisplayName}: reconnect in Settings");
                    }
                    else
                    {
                        _failedAt[source.Id] = DateTimeOffset.UtcNow;
                    }
                }
            }

            // Couldn't fetch: fall back to the last good copy, if there is one.
            if (LoadSaved(source) is { } saved)
            {
                stale.Add(saved.RetrievedAt);
                return Overlapping(saved.Events, rangeStart, rangeEnd);
            }

            return Array.Empty<CalendarEvent>();
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        Problems = problems.OrderBy(p => p).ToList();
        IsOffline = !stale.IsEmpty;
        StaleSince = IsOffline ? stale.Min() : null;
        var merged = results.SelectMany(r => r).OrderBy(e => e.Start).ToList();
        // Don't pin a degraded result in the in-memory cache: the next request should try the network again.
        _cache = IsOffline ? null : new EventCache(rangeStart, rangeEnd, merged);
        return merged;
    }

    static string KeyFor(CalendarSourceConfig source) => $"events_{source.Id}";

    SavedEvents? LoadSaved(CalendarSourceConfig source) => _disk?.Load<SavedEvents>(KeyFor(source));

    /// <summary>Keeps earlier-fetched events outside this range too, so browsing months online doesn't evict the others from the offline copy.</summary>
    void SaveFresh(CalendarSourceConfig source, IReadOnlyList<CalendarEvent> fresh, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (_disk is null)
        {
            return;
        }

        var keepAfter = DateTimeOffset.UtcNow.AddDays(-60);
        var kept = LoadSaved(source)?.Events.Where(e => !(e.Start < rangeEnd && e.End > rangeStart) && e.End > keepAfter) ?? Enumerable.Empty<CalendarEvent>();
        _disk.Save(KeyFor(source), new SavedEvents(DateTimeOffset.UtcNow, kept.Concat(fresh).ToList()));
    }

    static List<CalendarEvent> Overlapping(IEnumerable<CalendarEvent> events, DateTimeOffset start, DateTimeOffset end) =>
        events.Where(e => e.Start < end && e.End > start).ToList();

    /// <summary>What was last fetched successfully for one source, and when.</summary>
    public record SavedEvents(DateTimeOffset RetrievedAt, List<CalendarEvent> Events);

    record EventCache(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<CalendarEvent> Events)
    {
        readonly DateTimeOffset _retrievedAt = DateTimeOffset.UtcNow;
        public TimeSpan Age => DateTimeOffset.UtcNow - _retrievedAt;
        public bool Covers(DateTimeOffset start, DateTimeOffset end) => start >= Start && end <= End;
    }
}
