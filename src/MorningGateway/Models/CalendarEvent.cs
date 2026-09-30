namespace MorningGateway.Models;

public class CalendarEvent
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public string ColorHex { get; init; } = "#4C8BF5";

    public bool OccursOn(DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var dayEnd = dayStart.AddDays(1);
        var localStart = Start.LocalDateTime;
        var localEnd = End.LocalDateTime;
        return localStart < dayEnd && localEnd > dayStart;
    }
}
