namespace MorningGateway.Models;

/// <summary>One day cell in the month grid, pre-computed so the view has no logic to run.</summary>
public class MonthDayCell
{
    public required DateOnly Date { get; init; }
    public required bool IsCurrentMonth { get; init; }
    public required bool IsToday { get; init; }
    public string DayNumberText => Date.Day.ToString();
    public List<CalendarEvent> Events { get; init; } = new();

    public IEnumerable<CalendarEvent> VisibleEvents => Events.Take(3);
    public int OverflowCount => Math.Max(0, Events.Count - 3);
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowText => $"+{OverflowCount} more";
}
