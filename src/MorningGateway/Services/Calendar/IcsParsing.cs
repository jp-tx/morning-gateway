using Ical.Net;
using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Shared ICS-to-CalendarEvent expansion used by both the plain ICS/webcal
/// provider and the CalDAV provider (CalDAV ultimately hands back raw ICS
/// bodies too). Handles recurring events (RRULE) by expanding occurrences
/// that fall inside the requested window.
/// </summary>
public static class IcsParsing
{
    public static List<CalendarEvent> ParseEvents(string icsText, string sourceId, string colorHex, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var results = new List<CalendarEvent>();
        Ical.Net.Calendar calendar;
        try
        {
            calendar = Ical.Net.Calendar.Load(icsText);
        }
        catch (Exception)
        {
            return results;
        }

        foreach (var ev in calendar.Events)
        {
            IList<Ical.Net.DataTypes.Occurrence> occurrences;
            try
            {
                occurrences = ev.GetOccurrences(rangeStart.LocalDateTime, rangeEnd.LocalDateTime).ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var occurrence in occurrences)
            {
                var start = occurrence.Period.StartTime.AsDateTimeOffset;
                var end = occurrence.Period.EndTime.AsDateTimeOffset;

                results.Add(new CalendarEvent
                {
                    Id = $"{sourceId}:{ev.Uid}:{start.UtcTicks}",
                    SourceId = sourceId,
                    Title = string.IsNullOrWhiteSpace(ev.Summary) ? "(untitled)" : ev.Summary,
                    Location = ev.Location,
                    Description = ev.Description,
                    Start = start,
                    End = end,
                    IsAllDay = ev.IsAllDay,
                    ColorHex = colorHex,
                });
            }
        }

        return results;
    }
}
