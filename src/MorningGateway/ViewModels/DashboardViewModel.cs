using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorningGateway.Models;
using MorningGateway.Services.Calendar;
using MorningGateway.Services.Display;
using MorningGateway.Services.Updates;
using MorningGateway.Services.Weather;
using MorningGateway.Views;

namespace MorningGateway.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    readonly CalendarAggregatorService _calendarAggregator;
    readonly IWeatherService _weatherService;
    readonly SettingsStore _settings;
    readonly UpdateCoordinator _updates;

    IDispatcherTimer? _clockTimer;
    IDispatcherTimer? _refreshTimer;

    public DashboardViewModel(CalendarAggregatorService calendarAggregator, IWeatherService weatherService, SettingsStore settings, BurnInProtectionService burnIn, UpdateCoordinator updates)
    {
        _updates = updates;
        _updates.PropertyChanged += (_, _) => OnPropertyChanged(nameof(StatusNotice));
        _calendarAggregator = calendarAggregator;
        _weatherService = weatherService;
        _settings = settings;
        BurnIn = burnIn;

        FocusedMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        FocusedDay = DateOnly.FromDateTime(DateTime.Today);
        WeekdayHeaders = BuildWeekdayHeaders();
        ClockTime = DateTime.Now;

        _settings.Changed += OnSettingsChanged;
    }

    public BurnInProtectionService BurnIn { get; }

    public List<string> WeekdayHeaders { get; }

    [ObservableProperty]
    DateTime clockTime;

    [ObservableProperty]
    CalendarViewMode viewMode = CalendarViewMode.Month;

    [ObservableProperty]
    DateOnly focusedMonth;

    [ObservableProperty]
    DateOnly focusedDay;

    [ObservableProperty]
    ObservableCollection<MonthDayCell> monthCells = new();

    [ObservableProperty]
    ObservableCollection<CalendarEvent> dayTimedEvents = new();

    [ObservableProperty]
    ObservableCollection<CalendarEvent> dayAllDayEvents = new();

    [ObservableProperty]
    WeatherSnapshot? weather;

    [ObservableProperty]
    bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCalendarProblem), nameof(StatusNotice), nameof(HasStatusNotice))]
    string calendarProblem = string.Empty;

    public bool HasCalendarProblem => !string.IsNullOrEmpty(CalendarProblem);

    /// <summary>Top-bar notice: calendar problems and/or an available app update.</summary>
    public string StatusNotice => string.Join(" · ", new[] { CalendarProblem, _updates.Notice }.Where(t => !string.IsNullOrEmpty(t)));

    public bool HasStatusNotice => !string.IsNullOrEmpty(StatusNotice);

    public string FocusedMonthLabel => FocusedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public string FocusedDayLabel => FocusedDay.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    public bool IsMonthMode => ViewMode == CalendarViewMode.Month;

    public void StartClocksAndTimers()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _clockTimer ??= dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(15);
        _clockTimer.Tick -= OnClockTick;
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

        _refreshTimer ??= dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromMinutes(15);
        _refreshTimer.Tick -= OnRefreshTick;
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        BurnIn.Start();
    }

    void OnClockTick(object? sender, EventArgs e) => ClockTime = DateTime.Now;

    async void OnRefreshTick(object? sender, EventArgs e) => await RefreshAsync();

    async void OnSettingsChanged()
    {
        BurnIn.Start();
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.WhenAll(RefreshCalendarAsync(), RefreshWeatherAsync(), _updates.CheckIfDueAsync());
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task RefreshCalendarAsync()
    {
        var rangeStart = FocusedMonth.AddDays(-7).ToDateTime(TimeOnly.MinValue);
        var rangeEnd = FocusedMonth.AddMonths(1).AddDays(7).ToDateTime(TimeOnly.MinValue);
        var events = await _calendarAggregator.GetEventsAsync(rangeStart, rangeEnd).ConfigureAwait(false);

        var cells = BuildMonthCells(FocusedMonth, events, MonthDayCell.MaxVisibleForScale(_settings.UiScale));
        var dayEvents = events.Where(e => e.OccursOn(FocusedDay)).OrderBy(e => e.Start).ToList();

        var problem = string.Join(" · ", _calendarAggregator.Problems);

        MainThreadInvoke(() =>
        {
            CalendarProblem = problem;
            MonthCells = new ObservableCollection<MonthDayCell>(cells);
            DayAllDayEvents = new ObservableCollection<CalendarEvent>(dayEvents.Where(e => e.IsAllDay));
            DayTimedEvents = new ObservableCollection<CalendarEvent>(dayEvents.Where(e => !e.IsAllDay));
        });
    }

    async Task RefreshWeatherAsync()
    {
        try
        {
            var snapshot = await _weatherService.GetForecastAsync(
                _settings.WeatherLatitude, _settings.WeatherLongitude, _settings.WeatherLocationName, _settings.UseFahrenheit).ConfigureAwait(false);
            MainThreadInvoke(() => Weather = snapshot);
        }
        catch (Exception)
        {
            // Keep showing the last good snapshot if the network hiccups.
        }
    }

    [RelayCommand]
    void ToggleViewMode()
    {
        ViewMode = ViewMode == CalendarViewMode.Month ? CalendarViewMode.Day : CalendarViewMode.Month;
        OnPropertyChanged(nameof(IsMonthMode));
    }

    [RelayCommand]
    async Task GoToTodayAsync()
    {
        FocusedMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        FocusedDay = DateOnly.FromDateTime(DateTime.Today);
        OnPropertyChanged(nameof(FocusedMonthLabel));
        OnPropertyChanged(nameof(FocusedDayLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    async Task PreviousAsync()
    {
        if (ViewMode == CalendarViewMode.Month)
        {
            FocusedMonth = FocusedMonth.AddMonths(-1);
            OnPropertyChanged(nameof(FocusedMonthLabel));
        }
        else
        {
            FocusedDay = FocusedDay.AddDays(-1);
            OnPropertyChanged(nameof(FocusedDayLabel));
        }

        await RefreshCalendarAsync();
    }

    [RelayCommand]
    async Task NextAsync()
    {
        if (ViewMode == CalendarViewMode.Month)
        {
            FocusedMonth = FocusedMonth.AddMonths(1);
            OnPropertyChanged(nameof(FocusedMonthLabel));
        }
        else
        {
            FocusedDay = FocusedDay.AddDays(1);
            OnPropertyChanged(nameof(FocusedDayLabel));
        }

        await RefreshCalendarAsync();
    }

    [RelayCommand]
    async Task SelectDayAsync(MonthDayCell? cell)
    {
        if (cell is null)
        {
            return;
        }

        FocusedDay = cell.Date;
        ViewMode = CalendarViewMode.Day;
        OnPropertyChanged(nameof(IsMonthMode));
        OnPropertyChanged(nameof(FocusedDayLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    async Task OpenSettingsAsync()
    {
        if (Shell.Current is not null)
        {
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }
    }

    static List<MonthDayCell> BuildMonthCells(DateOnly monthStart, IReadOnlyList<CalendarEvent> events, int maxVisible)
    {
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)monthStart.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var gridStart = monthStart.AddDays(-offset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var cells = new List<MonthDayCell>(42);
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var dayEvents = events.Where(e => e.OccursOn(date)).OrderBy(e => e.IsAllDay ? DateTimeOffset.MinValue : e.Start).ToList();
            cells.Add(new MonthDayCell
            {
                Date = date,
                IsCurrentMonth = date.Month == monthStart.Month,
                IsToday = date == today,
                Events = dayEvents,
                MaxVisible = maxVisible,
            });
        }

        return cells;
    }

    static List<string> BuildWeekdayHeaders()
    {
        var firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        var ordered = new List<string>(7);
        for (var i = 0; i < 7; i++)
        {
            ordered.Add(names[((int)firstDayOfWeek + i) % 7]);
        }

        return ordered;
    }

    static void MainThreadInvoke(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }

    public void Dispose()
    {
        _clockTimer?.Stop();
        _refreshTimer?.Stop();
        BurnIn.Stop();
        _settings.Changed -= OnSettingsChanged;
    }
}
