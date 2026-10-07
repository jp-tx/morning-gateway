using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorningGateway.Models;
using MorningGateway.Services.Calendar;
using MorningGateway.Services.Diagnostics;
using MorningGateway.Services.Display;
using MorningGateway.Services.Net;
using MorningGateway.Services.Updates;
using MorningGateway.Services.Weather;
using MorningGateway.Views;

namespace MorningGateway.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    readonly CalendarAggregatorService _calendarAggregator;
    readonly IWeatherService _weatherService;
    readonly SettingsStore _settings;
    readonly UpdateCoordinator _updates;
    readonly JsonFileCache _cache;

    const string WeatherCacheKey = "weather_snapshot";
    static readonly TimeSpan NormalRefreshInterval = TimeSpan.FromMinutes(15);
    static readonly TimeSpan OfflineRetryInterval = TimeSpan.FromMinutes(1);

    bool _paintedSavedData;
    bool _weatherStale;
    bool _refreshQueued;
    string? _lastWeatherError;

    /// <summary>Bumped for every calendar load so a slow, older load can't paint over a newer one.</summary>
    int _calendarGeneration;

    /// <summary>What the month grid currently shows; lets an unchanged refresh skip rebuilding 42 cells.</summary>
    int? _monthSignature;

    DateOnly _today;

    IDispatcherTimer? _clockTimer;
    IDispatcherTimer? _refreshTimer;

    public DashboardViewModel(CalendarAggregatorService calendarAggregator, IWeatherService weatherService, SettingsStore settings, BurnInProtectionService burnIn, UpdateCoordinator updates, JsonFileCache cache)
    {
        _cache = cache;
        _updates = updates;
        _calendarAggregator = calendarAggregator;
        _weatherService = weatherService;
        _settings = settings;
        BurnIn = burnIn;

        _today = DateOnly.FromDateTime(DateTime.Today);
        FocusedMonth = new DateOnly(_today.Year, _today.Month, 1);
        FocusedDay = _today;
        WeekdayHeaders = BuildWeekdayHeaders();
        ClockTime = DateTime.Now;
    }

    public BurnInProtectionService BurnIn { get; }

    public List<string> WeekdayHeaders { get; }

    [ObservableProperty]
    DateTime clockTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonthMode))]
    CalendarViewMode viewMode = CalendarViewMode.Month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedMonthLabel))]
    DateOnly focusedMonth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedDayLabel))]
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
    public string StatusNotice => string.Join(" · ", new[] { CalendarProblem, OfflineNotice, _updates.Notice }.Where(t => !string.IsNullOrEmpty(t)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusNotice), nameof(HasStatusNotice))]
    string offlineNotice = string.Empty;

    public bool HasStatusNotice => !string.IsNullOrEmpty(StatusNotice);

    public string FocusedMonthLabel => FocusedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    public string FocusedDayLabel => FocusedDay.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d", CultureInfo.CurrentCulture);

    public bool IsMonthMode => ViewMode == CalendarViewMode.Month;

    /// <summary>Called whenever the dashboard page appears. Safe to call repeatedly.</summary>
    public void StartClocksAndTimers()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _settings.Changed -= OnSettingsChanged;
        _settings.Changed += OnSettingsChanged;
        _updates.PropertyChanged -= OnUpdatesChanged;
        _updates.PropertyChanged += OnUpdatesChanged;

        _clockTimer ??= dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(15);
        _clockTimer.Tick -= OnClockTick;
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

        _refreshTimer ??= dispatcher.CreateTimer();
        _refreshTimer.Interval = NormalRefreshInterval;
        _refreshTimer.Tick -= OnRefreshTick;
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        BurnIn.Start();
        OnClockTick(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called whenever the dashboard page goes away (Settings opened, activity destroyed). The services this
    /// subscribes to outlive the page, so leaving timers and handlers attached would keep driving a dead page.
    /// </summary>
    public void Stop()
    {
        _clockTimer?.Stop();
        _refreshTimer?.Stop();
        BurnIn.Stop();
        _settings.Changed -= OnSettingsChanged;
        _updates.PropertyChanged -= OnUpdatesChanged;
    }

    void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusNotice));
        OnPropertyChanged(nameof(HasStatusNotice));
    }

    void OnClockTick(object? sender, EventArgs e)
    {
        try
        {
            ClockTime = DateTime.Now;

            var today = DateOnly.FromDateTime(DateTime.Today);
            if (today == _today)
            {
                return;
            }

            // Midnight: a display left showing "today" / "this month" follows the date instead of going stale.
            var previous = _today;
            _today = today;
            if (FocusedDay == previous)
            {
                FocusedDay = today;
            }

            if (FocusedMonth == new DateOnly(previous.Year, previous.Month, 1))
            {
                FocusedMonth = new DateOnly(today.Year, today.Month, 1);
            }

            _ = RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Clock tick failed", ex);
        }
    }

    // async void handlers: anything thrown here would take the whole app down, so RefreshAsync never throws.
    async void OnRefreshTick(object? sender, EventArgs e) => await RefreshAsync();

    async void OnSettingsChanged()
    {
        try
        {
            BurnIn.Start();
        }
        catch (Exception)
        {
            // Burn-in protection is cosmetic; still refresh.
        }

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            // Don't drop the request: whatever prompted it (a settings change, the page reappearing)
            // may postdate the values the running refresh started with.
            _refreshQueued = true;
            return;
        }

        IsBusy = true;
        try
        {
            do
            {
                _refreshQueued = false;
                try
                {
                    // Each part handles its own failures; the guard here is the last line of defence.
                    await Task.WhenAll(RefreshCalendarAsync(), RefreshWeatherAsync(), CheckForUpdateAsync());
                }
                catch (Exception ex)
                {
                    CrashLog.Write("Refresh failed", ex);
                }
            }
            while (_refreshQueued);
        }
        finally
        {
            IsBusy = false;
            // Poll quickly while offline so data comes back soon after the network does.
            if (_refreshTimer is not null)
            {
                _refreshTimer.Interval = OfflineNotice.Length > 0 || _weatherStale ? OfflineRetryInterval : NormalRefreshInterval;
            }
        }
    }

    async Task CheckForUpdateAsync()
    {
        try
        {
            await _updates.CheckIfDueAsync();
        }
        catch (Exception)
        {
            // Update checks are best-effort.
        }
    }

    async Task RefreshCalendarAsync()
    {
        var generation = ++_calendarGeneration;
        try
        {
            // The month grid is always six weeks and can start up to six days before the 1st,
            // so fetch the whole visible grid (plus slack), not just the calendar month.
            var rangeStart = FocusedMonth.AddDays(-7).ToDateTime(TimeOnly.MinValue);
            var rangeEnd = FocusedMonth.AddDays(49).ToDateTime(TimeOnly.MinValue);

            if (!_paintedSavedData)
            {
                // First load after launch: show the saved copy right away instead of waiting on a slow or dead network.
                _paintedSavedData = true;
                ApplyCalendar(generation, _calendarAggregator.GetSavedEvents(rangeStart, rangeEnd), string.Empty, string.Empty);
            }

            var events = await _calendarAggregator.GetEventsAsync(rangeStart, rangeEnd).ConfigureAwait(false);
            var offline = _calendarAggregator.IsOffline
                ? $"Offline - calendar as of {_calendarAggregator.StaleSince?.ToLocalTime():MMM d, h:mm tt}"
                : string.Empty;
            ApplyCalendar(generation, events, string.Join(" · ", _calendarAggregator.Problems), offline);
        }
        catch (Exception ex)
        {
            // Never let a refresh failure escape; whatever is on screen stays.
            CrashLog.Write("Calendar refresh failed", ex);
        }
    }

    void ApplyCalendar(int generation, IReadOnlyList<CalendarEvent> events, string problem, string offline)
    {
        MainThreadInvoke(() =>
        {
            try
            {
                if (generation != _calendarGeneration)
                {
                    // The user has navigated since this load started; these events are for another range.
                    return;
                }

                CalendarProblem = problem;
                OfflineNotice = offline;

                var maxVisible = MonthDayCell.MaxVisibleForScale(_settings.UiScale);
                var signature = MonthSignature(FocusedMonth, maxVisible, events);
                if (signature != _monthSignature)
                {
                    _monthSignature = signature;
                    MonthCells = new ObservableCollection<MonthDayCell>(BuildMonthCells(FocusedMonth, events, maxVisible));
                }

                var dayEvents = events.Where(e => e.OccursOn(FocusedDay)).OrderBy(e => e.Start).ToList();
                DayAllDayEvents = new ObservableCollection<CalendarEvent>(dayEvents.Where(e => e.IsAllDay));
                DayTimedEvents = new ObservableCollection<CalendarEvent>(dayEvents.Where(e => !e.IsAllDay));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Painting the calendar failed", ex);
            }
        });
    }

    static int MonthSignature(DateOnly month, int maxVisible, IReadOnlyList<CalendarEvent> events)
    {
        var hash = new HashCode();
        hash.Add(month);
        hash.Add(DateTime.Today);
        hash.Add(maxVisible);
        foreach (var e in events)
        {
            hash.Add(e.Id);
            hash.Add(e.Title);
            hash.Add(e.Start);
            hash.Add(e.End);
            hash.Add(e.IsAllDay);
            hash.Add(e.ColorHex);
        }

        return hash.ToHashCode();
    }

    async Task RefreshWeatherAsync()
    {
        try
        {
            if (Weather is null && LoadSavedWeather() is { } saved)
            {
                MainThreadInvoke(() => Weather = saved);
            }

            var snapshot = await _weatherService.GetForecastAsync(
                _settings.WeatherLatitude, _settings.WeatherLongitude, _settings.WeatherLocationName, _settings.UseFahrenheit).ConfigureAwait(false);
            _weatherStale = false;
            _lastWeatherError = null;
            _cache.Save(WeatherCacheKey, snapshot);
            MainThreadInvoke(() => Weather = snapshot);
        }
        catch (Exception ex)
        {
            // Keep showing the last good snapshot (in memory or saved) if the network hiccups.
            _weatherStale = true;
            if (_lastWeatherError != ex.Message)
            {
                // Retried every minute while failing, so log each distinct failure only once.
                _lastWeatherError = ex.Message;
                CrashLog.Write("Weather refresh failed", ex);
            }
        }
    }

    /// <summary>The saved forecast, only if it is for the currently configured place and unit.</summary>
    WeatherSnapshot? LoadSavedWeather()
    {
        var saved = _cache.Load<WeatherSnapshot>(WeatherCacheKey);
        var unit = _settings.UseFahrenheit ? "°F" : "°C";
        return saved is not null && saved.LocationName == _settings.WeatherLocationName && saved.UnitSuffix == unit ? saved : null;
    }

    [RelayCommand]
    void ToggleViewMode()
    {
        ViewMode = ViewMode == CalendarViewMode.Month ? CalendarViewMode.Day : CalendarViewMode.Month;
    }

    [RelayCommand]
    async Task GoToTodayAsync()
    {
        FocusedMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        FocusedDay = DateOnly.FromDateTime(DateTime.Today);
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    Task PreviousAsync() => StepAsync(-1);

    [RelayCommand]
    Task NextAsync() => StepAsync(1);

    async Task StepAsync(int direction)
    {
        if (ViewMode == CalendarViewMode.Month)
        {
            FocusedMonth = FocusedMonth.AddMonths(direction);
        }
        else
        {
            FocusedDay = FocusedDay.AddDays(direction);
            // Events are loaded per month, so stepping day by day has to bring the month along.
            FocusedMonth = new DateOnly(FocusedDay.Year, FocusedDay.Month, 1);
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
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    async Task OpenSettingsAsync()
    {
        try
        {
            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync(nameof(SettingsPage));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Opening Settings failed", ex);
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
}
