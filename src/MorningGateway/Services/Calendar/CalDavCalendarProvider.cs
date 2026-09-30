using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

public class CalDavCalendarProvider : ICalendarProvider
{
    readonly CalDavClient _client;

    public CalDavCalendarProvider(CalDavClient client)
    {
        _client = client;
    }

    public CalendarSourceType Type => CalendarSourceType.CalDav;

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarSourceConfig source, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.CalDavServerUrl) || string.IsNullOrWhiteSpace(source.CalDavUsername))
        {
            return Array.Empty<CalendarEvent>();
        }

        var password = await SecureStorage.Default.GetAsync(source.SecureStorageKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(password))
        {
            return Array.Empty<CalendarEvent>();
        }

        var icsBodies = await _client.QueryEventsAsync(source.CalDavServerUrl, source.CalDavUsername, password, rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);

        var events = new List<CalendarEvent>();
        foreach (var ics in icsBodies)
        {
            events.AddRange(IcsParsing.ParseEvents(ics, source.Id, source.ColorHex, rangeStart, rangeEnd));
        }

        return events;
    }
}
