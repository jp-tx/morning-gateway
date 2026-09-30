using System.Collections;
using Microsoft.Maui.Layouts;
using MorningGateway.Models;

namespace MorningGateway.Views;

public partial class DayCalendarView : ContentView
{
    const double HourHeight = 60;
    const double MinBlockHeight = 26;

    public static readonly BindableProperty DayProperty =
        BindableProperty.Create(nameof(Day), typeof(DateOnly), typeof(DayCalendarView), propertyChanged: OnAnyDataChanged);

    public static readonly BindableProperty TimedEventsProperty =
        BindableProperty.Create(nameof(TimedEvents), typeof(IEnumerable), typeof(DayCalendarView), propertyChanged: OnAnyDataChanged);

    public static readonly BindableProperty AllDayEventsProperty =
        BindableProperty.Create(nameof(AllDayEvents), typeof(IEnumerable), typeof(DayCalendarView), propertyChanged: OnAnyDataChanged);

    public DayCalendarView()
    {
        InitializeComponent();
        BuildHourLabels();
    }

    public DateOnly Day
    {
        get => (DateOnly)GetValue(DayProperty);
        set => SetValue(DayProperty, value);
    }

    public IEnumerable? TimedEvents
    {
        get => (IEnumerable?)GetValue(TimedEventsProperty);
        set => SetValue(TimedEventsProperty, value);
    }

    public IEnumerable? AllDayEvents
    {
        get => (IEnumerable?)GetValue(AllDayEventsProperty);
        set => SetValue(AllDayEventsProperty, value);
    }

    static void OnAnyDataChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((DayCalendarView)bindable).Rebuild();

    void BuildHourLabels()
    {
        HourLabelsColumn.Children.Clear();
        for (var hour = 0; hour < 24; hour++)
        {
            var label = new Label
            {
                Text = DateTime.Today.AddHours(hour).ToString("h tt"),
                FontSize = 11,
                HeightRequest = HourHeight,
                HorizontalTextAlignment = TextAlignment.End,
                Padding = new Thickness(0, 4, 8, 0),
            };
            label.SetAppThemeColor(Label.TextColorProperty, (Color)Application.Current!.Resources["LightTextSecondary"], (Color)Application.Current!.Resources["DarkTextSecondary"]);
            HourLabelsColumn.Children.Add(label);
        }
    }

    void Rebuild()
    {
        BuildAllDayRow();
        BuildTimedCanvas();
    }

    void BuildAllDayRow()
    {
        AllDayRow.Children.Clear();
        foreach (var evt in (AllDayEvents as IEnumerable<CalendarEvent>) ?? Enumerable.Empty<CalendarEvent>())
        {
            var chip = new Border
            {
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb(evt.ColorHex),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Content = new Label { Text = evt.Title, TextColor = Colors.White, FontSize = 13 },
            };
            AllDayRow.Children.Add(chip);
        }
    }

    void BuildTimedCanvas()
    {
        EventCanvas.Children.Clear();
        EventCanvas.HeightRequest = HourHeight * 24;

        for (var hour = 0; hour < 24; hour++)
        {
            var divider = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Start };
            divider.SetAppThemeColor(BoxView.ColorProperty, (Color)Application.Current!.Resources["LightDivider"], (Color)Application.Current!.Resources["DarkDivider"]);
            AbsoluteLayout.SetLayoutBounds(divider, new Rect(0, hour * HourHeight, 1, 1));
            AbsoluteLayout.SetLayoutFlags(divider, AbsoluteLayoutFlags.WidthProportional);
            EventCanvas.Children.Add(divider);
        }

        var events = ((TimedEvents as IEnumerable<CalendarEvent>) ?? Enumerable.Empty<CalendarEvent>())
            .OrderBy(e => e.Start)
            .ToList();

        foreach (var (evt, lane, laneCount) in AssignLanes(events))
        {
            var dayStart = Day.ToDateTime(TimeOnly.MinValue);
            var startMinutes = Math.Max(0, (evt.Start.LocalDateTime - dayStart).TotalMinutes);
            var endMinutes = Math.Min(24 * 60, (evt.End.LocalDateTime - dayStart).TotalMinutes);
            var top = startMinutes / 60.0 * HourHeight;
            var height = Math.Max(MinBlockHeight, (endMinutes - startMinutes) / 60.0 * HourHeight);

            var block = new Border
            {
                Padding = new Thickness(6, 3),
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb(evt.ColorHex),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Content = new VerticalStackLayout
                {
                    Spacing = 1,
                    Children =
                    {
                        new Label { Text = evt.Title, TextColor = Colors.White, FontSize = 12, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation },
                        new Label { Text = evt.Start.ToString("h:mm tt"), TextColor = Colors.White, FontSize = 10, Opacity = 0.85 },
                    },
                },
            };

            AbsoluteLayout.SetLayoutBounds(block, new Rect(1.0 / laneCount * lane, top, 1.0 / laneCount, height));
            AbsoluteLayout.SetLayoutFlags(block, AbsoluteLayoutFlags.XProportional | AbsoluteLayoutFlags.WidthProportional);
            EventCanvas.Children.Add(block);
        }

        if (Day == DateOnly.FromDateTime(DateTime.Today))
        {
            var nowMinutes = DateTime.Now.TimeOfDay.TotalMinutes;
            var nowLine = new BoxView { HeightRequest = 2, Color = (Color)Application.Current!.Resources["Danger"] };
            AbsoluteLayout.SetLayoutBounds(nowLine, new Rect(0, nowMinutes / 60.0 * HourHeight, 1, 2));
            AbsoluteLayout.SetLayoutFlags(nowLine, AbsoluteLayoutFlags.WidthProportional);
            EventCanvas.Children.Add(nowLine);
        }
    }

    /// <summary>
    /// Greedy interval-scheduling lane assignment so simultaneous events sit
    /// side by side instead of stacking on top of one another, the same
    /// approach Google/Outlook calendar day views use.
    /// </summary>
    static IEnumerable<(CalendarEvent Event, int Lane, int LaneCount)> AssignLanes(List<CalendarEvent> sortedEvents)
    {
        // Pass 1: greedily assign each event the first free lane. Pass 2: for
        // each event, widen its effective lane count to the max lane index used
        // by anything it directly overlaps, so a whole overlapping cluster
        // shares equal-width columns.
        var lanes = new List<int>(sortedEvents.Count);
        var laneEndTimes = new List<DateTimeOffset>();
        foreach (var evt in sortedEvents)
        {
            var laneIndex = laneEndTimes.FindIndex(end => end <= evt.Start);
            if (laneIndex < 0)
            {
                laneIndex = laneEndTimes.Count;
                laneEndTimes.Add(evt.End);
            }
            else
            {
                laneEndTimes[laneIndex] = evt.End;
            }

            lanes.Add(laneIndex);
        }

        // Determine, for each event, the max lane index concurrently in use across
        // events it overlaps with, to size lane width per overlapping cluster.
        var laneCounts = new int[sortedEvents.Count];
        for (var i = 0; i < sortedEvents.Count; i++)
        {
            var maxLane = lanes[i];
            for (var j = 0; j < sortedEvents.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var overlaps = sortedEvents[i].Start < sortedEvents[j].End && sortedEvents[j].Start < sortedEvents[i].End;
                if (overlaps)
                {
                    maxLane = Math.Max(maxLane, lanes[j]);
                }
            }

            laneCounts[i] = maxLane + 1;
        }

        for (var i = 0; i < sortedEvents.Count; i++)
        {
            yield return (sortedEvents[i], lanes[i], laneCounts[i]);
        }
    }
}
