using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MorningGateway.Models;
using MorningGateway.Services.Calendar;
using MorningGateway.Services.Display;
using MorningGateway.Services.Updates;
using MorningGateway.Services.Weather;

namespace MorningGateway.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    readonly CalendarSourceStore _sourceStore;
    readonly GoogleCalendarProvider _googleProvider;
    readonly CalDavClient _calDavClient;
    readonly IWeatherService _weatherService;
    readonly DeviceLocationService _locationService;

    public SettingsViewModel(SettingsStore settings, CalendarSourceStore sourceStore, GoogleCalendarProvider googleProvider, CalDavClient calDavClient, IWeatherService weatherService, DeviceLocationService locationService, UpdateCoordinator updates)
    {
        Updates = updates;
        Settings = settings;
        _sourceStore = sourceStore;
        _googleProvider = googleProvider;
        _calDavClient = calDavClient;
        _weatherService = weatherService;
        _locationService = locationService;

        Sources = new ObservableCollection<CalendarSourceConfig>(_sourceStore.Load());
        LocationSearchText = settings.WeatherLocationName;

        NewIcsColor = "#4C8BF5";
        NewCalDavColor = "#34A853";
    }

    public SettingsStore Settings { get; }

    public UpdateCoordinator Updates { get; }

    [RelayCommand]
    Task CheckForUpdatesAsync() => Updates.CheckAsync();

    [RelayCommand]
    Task InstallUpdateAsync() => Updates.InstallAsync();

    public List<string> ThemeOptions { get; } = new() { "System", "Light", "Dark" };

    public string ThemeModeText
    {
        get => Settings.ThemeMode.ToString();
        set
        {
            if (Enum.TryParse<AppThemeMode>(value, out var parsed))
            {
                Settings.ThemeMode = parsed;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<CalendarSourceConfig> Sources { get; }

    public ObservableCollection<GeocodeResult> LocationResults { get; } = new();

    public ObservableCollection<DiscoveredCalendar> DiscoveredCalDavCalendars { get; } = new();

    [ObservableProperty]
    string statusMessage = string.Empty;

    [ObservableProperty]
    bool isBusy;

    // --- Add ICS calendar ---
    [ObservableProperty]
    string newIcsName = string.Empty;

    [ObservableProperty]
    string newIcsUrl = string.Empty;

    [ObservableProperty]
    string newIcsColor;

    [RelayCommand]
    void AddIcsSource()
    {
        if (string.IsNullOrWhiteSpace(NewIcsUrl))
        {
            StatusMessage = "Enter a calendar URL first.";
            return;
        }

        var source = new CalendarSourceConfig
        {
            DisplayName = string.IsNullOrWhiteSpace(NewIcsName) ? "Calendar" : NewIcsName,
            Type = CalendarSourceType.IcsUrl,
            IcsUrl = NewIcsUrl.Trim(),
            ColorHex = NewIcsColor,
        };

        _sourceStore.Upsert(source);
        Sources.Add(source);
        NewIcsName = string.Empty;
        NewIcsUrl = string.Empty;
        StatusMessage = $"Added \"{source.DisplayName}\".";
    }

    // --- Add Google calendar ---
    [RelayCommand]
    async Task ConnectGoogleAsync()
    {
        IsBusy = true;
        StatusMessage = "Opening Google sign-in...";
        try
        {
            var source = new CalendarSourceConfig
            {
                DisplayName = "Google Calendar",
                Type = CalendarSourceType.Google,
                ColorHex = "#4285F4",
            };

            var email = await _googleProvider.SignInAsync(source);
            source.DisplayName = email;
            source.GoogleAccountEmail = email;

            _sourceStore.Upsert(source);
            Sources.Add(source);
            StatusMessage = $"Connected Google account {email}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Google sign-in cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Google sign-in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Add CalDAV calendar ---
    [ObservableProperty]
    string newCalDavServerUrl = string.Empty;

    [ObservableProperty]
    string newCalDavUsername = string.Empty;

    [ObservableProperty]
    string newCalDavPassword = string.Empty;

    [ObservableProperty]
    string newCalDavColor;

    [RelayCommand]
    async Task DiscoverCalDavCalendarsAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCalDavServerUrl) || string.IsNullOrWhiteSpace(NewCalDavUsername) || string.IsNullOrWhiteSpace(NewCalDavPassword))
        {
            StatusMessage = "Fill in server URL, username and password first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Looking for calendars...";
        try
        {
            DiscoveredCalDavCalendars.Clear();
            var found = await _calDavClient.DiscoverCalendarsAsync(NewCalDavServerUrl.Trim(), NewCalDavUsername.Trim(), NewCalDavPassword);
            foreach (var cal in found)
            {
                DiscoveredCalDavCalendars.Add(cal);
            }

            StatusMessage = found.Count == 0 ? "No calendars found - double check the server URL and credentials." : $"Found {found.Count} calendar(s). Tap one to add it.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Discovery failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    async Task AddCalDavCalendarAsync(DiscoveredCalendar? calendar)
    {
        if (calendar is null)
        {
            return;
        }

        var source = new CalendarSourceConfig
        {
            DisplayName = calendar.DisplayName,
            Type = CalendarSourceType.CalDav,
            CalDavServerUrl = calendar.Url,
            CalDavUsername = NewCalDavUsername.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(calendar.ColorHex) ? NewCalDavColor : calendar.ColorHex!,
        };

        await SecureStorage.Default.SetAsync(source.SecureStorageKey, NewCalDavPassword);
        _sourceStore.Upsert(source);
        Sources.Add(source);
        StatusMessage = $"Added \"{source.DisplayName}\".";
    }

    // --- Manage existing sources ---
    [RelayCommand]
    async Task RemoveSourceAsync(CalendarSourceConfig? source)
    {
        if (source is null)
        {
            return;
        }

        await _sourceStore.RemoveAsync(source.Id);
        Sources.Remove(source);
        StatusMessage = $"Removed \"{source.DisplayName}\".";
    }

    /// <summary>
    /// Called from the Sources list's Switch.Toggled handler: the switch is
    /// two-way bound straight to CalendarSourceConfig.Enabled, so by the time
    /// this runs the in-memory object already reflects the new state - this
    /// just persists it.
    /// </summary>
    public void PersistSourceState(CalendarSourceConfig source) => _sourceStore.Upsert(source);

    // --- Weather location ---
    [ObservableProperty]
    string locationSearchText;

    [RelayCommand]
    async Task SearchLocationAsync()
    {
        IsBusy = true;
        try
        {
            LocationResults.Clear();
            var results = await _weatherService.SearchLocationAsync(LocationSearchText);
            foreach (var r in results)
            {
                LocationResults.Add(r);
            }

            if (results.Count == 0)
            {
                StatusMessage = "No matching locations.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    void SelectLocation(GeocodeResult? result)
    {
        if (result is null)
        {
            return;
        }

        Settings.WeatherLatitude = result.Latitude;
        Settings.WeatherLongitude = result.Longitude;
        Settings.WeatherLocationName = string.IsNullOrWhiteSpace(result.Region) ? $"{result.Name}, {result.Country}" : $"{result.Name}, {result.Region}";
        LocationSearchText = Settings.WeatherLocationName;
        LocationResults.Clear();
        StatusMessage = $"Weather location set to {Settings.WeatherLocationName}.";
    }

    [RelayCommand]
    async Task UseDeviceLocationAsync()
    {
        IsBusy = true;
        try
        {
            var location = await _locationService.TryGetCurrentLocationAsync();
            if (location is null)
            {
                StatusMessage = "Couldn't get the device's location - check location permission.";
                return;
            }

            Settings.WeatherLatitude = location.Latitude;
            Settings.WeatherLongitude = location.Longitude;
            StatusMessage = "Using the device's current coordinates. Search above to set a friendly place name.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
