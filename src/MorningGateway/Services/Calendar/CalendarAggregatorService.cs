using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Fans a date-range query out across every enabled calendar source (ICS,
/// Google, CalDAV), merges the results, and caches them briefly so switching
/// between month/day view doesn't re-hit the network every time.
/// </summary>
public class CalendarAggregatorService
{
    readonly CalendarSourceStore _store;
    readonly IReadOnlyDictionary<CalendarSourceType, ICalendarProvider> _providers;

    EventCache? _cache;

    /// <summary>Per-source problems from the most recent fetch (e.g. "me@x.com: reconnect the account in Settings"); empty when all is well.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    public CalendarAggregatorService(CalendarSourceStore store, IEnumerable<ICalendarProvider> providers)
    {
        _store = store;
        _providers = providers.ToDictionary(p => p.Type);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cache is { } cache && cache.Covers(rangeStart, rangeEnd) && cache.Age < TimeSpan.FromMinutes(5))
        {
            return cache.Events;
        }

        var sources = _store.Load().Where(s => s.Enabled).ToList();
        var problems = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = sources.Select(async source =>
        {
            if (!_providers.TryGetValue(source.Type, out var provider))
            {
                return Array.Empty<CalendarEvent>();
            }

            try
            {
                return (IReadOnlyList<CalendarEvent>)await provider.GetEventsAsync(source, rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // One failing calendar (bad credentials, offline, etc.) shouldn't blank the whole dashboard.
                if (ex is GoogleReauthRequiredException)
                {
                    problems.Add($"{source.DisplayName}: reconnect in Settings");
                }

                return Array.Empty<CalendarEvent>();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        Problems = problems.OrderBy(p => p).ToList();
        var merged = results.SelectMany(r => r).OrderBy(e => e.Start).ToList();
        _cache = new EventCache(rangeStart, rangeEnd, merged);
        return merged;
    }

    record EventCache(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<CalendarEvent> Events)
    {
        readonly DateTimeOffset _retrievedAt = DateTimeOffset.UtcNow;
        public TimeSpan Age => DateTimeOffset.UtcNow - _retrievedAt;
        public bool Covers(DateTimeOffset start, DateTimeOffset end) => start >= Start && end <= End;
    }
}
