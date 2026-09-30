using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

public interface ICalendarProvider
{
    CalendarSourceType Type { get; }

    /// <summary>Fetches events overlapping [rangeStart, rangeEnd) for the given source.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarSourceConfig source, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);
}
