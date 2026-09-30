using System.Collections;
using System.Windows.Input;

namespace MorningGateway.Views;

public partial class MonthCalendarView : ContentView
{
    public static readonly BindableProperty CellsProperty =
        BindableProperty.Create(nameof(Cells), typeof(IEnumerable), typeof(MonthCalendarView));

    public static readonly BindableProperty WeekdayHeadersProperty =
        BindableProperty.Create(nameof(WeekdayHeaders), typeof(IEnumerable), typeof(MonthCalendarView));

    public static readonly BindableProperty SelectDayCommandProperty =
        BindableProperty.Create(nameof(SelectDayCommand), typeof(ICommand), typeof(MonthCalendarView));

    public MonthCalendarView()
    {
        InitializeComponent();
    }

    public IEnumerable? Cells
    {
        get => (IEnumerable?)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public IEnumerable? WeekdayHeaders
    {
        get => (IEnumerable?)GetValue(WeekdayHeadersProperty);
        set => SetValue(WeekdayHeadersProperty, value);
    }

    public ICommand? SelectDayCommand
    {
        get => (ICommand?)GetValue(SelectDayCommandProperty);
        set => SetValue(SelectDayCommandProperty, value);
    }
}
