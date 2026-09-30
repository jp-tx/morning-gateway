using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Reads a "secret address in iCal format" (Google Calendar) or a public
/// iCloud calendar share link. Read-only, no auth beyond an unguessable URL.
/// </summary>
public class IcsUrlCalendarProvider : ICalendarProvider
{
    readonly HttpClient _http;

    public IcsUrlCalendarProvider(HttpClient http)
    {
        _http = http;
    }

    public CalendarSourceType Type => CalendarSourceType.IcsUrl;

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarSourceConfig source, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.IcsUrl))
        {
            return Array.Empty<CalendarEvent>();
        }

        var url = source.IcsUrl.Replace("webcal://", "https://", StringComparison.OrdinalIgnoreCase);
        var icsText = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return IcsParsing.ParseEvents(icsText, source.Id, source.ColorHex, rangeStart, rangeEnd);
    }
}
