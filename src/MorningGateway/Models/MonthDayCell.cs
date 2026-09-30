namespace MorningGateway.Models;

/// <summary>One day cell in the month grid, pre-computed so the view has no logic to run.</summary>
public class MonthDayCell
{
    public required DateOnly Date { get; init; }
    public required bool IsCurrentMonth { get; init; }
    public required bool IsToday { get; init; }
    public string DayNumberText => Date.Day.ToString();
    public List<CalendarEvent> Events { get; init; } = new();

    /// <summary>How many event chips fit in a cell; fewer at larger UI scales so the six-week grid still fits the screen.</summary>
    public int MaxVisible { get; init; } = 3;

    public static int MaxVisibleForScale(double uiScale) => Math.Max(1, (int)Math.Floor(3.2 / Math.Max(uiScale, 0.5)));

    public IEnumerable<CalendarEvent> VisibleEvents => Events.Take(MaxVisible);
    public int OverflowCount => Math.Max(0, Events.Count - MaxVisible);
    public bool HasOverflow => OverflowCount > 0;
    public string OverflowText => $"+{OverflowCount} more";
}
