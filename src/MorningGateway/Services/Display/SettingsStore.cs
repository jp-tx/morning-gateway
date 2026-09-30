namespace MorningGateway.Services.Display;

public enum AppThemeMode { System, Light, Dark }

/// <summary>Thin typed wrapper around MAUI Preferences for all user-configurable dashboard settings.</summary>
public class SettingsStore
{
    public event Action? Changed;

    public bool KeepScreenOn
    {
        get => Preferences.Default.Get(nameof(KeepScreenOn), true);
        set { Preferences.Default.Set(nameof(KeepScreenOn), value); Changed?.Invoke(); }
    }

    public bool BurnInProtectionEnabled
    {
        get => Preferences.Default.Get(nameof(BurnInProtectionEnabled), true);
        set { Preferences.Default.Set(nameof(BurnInProtectionEnabled), value); Changed?.Invoke(); }
    }

    public AppThemeMode ThemeMode
    {
        get => Enum.TryParse<AppThemeMode>(Preferences.Default.Get(nameof(ThemeMode), nameof(AppThemeMode.System)), out var v) ? v : AppThemeMode.System;
        set { Preferences.Default.Set(nameof(ThemeMode), value.ToString()); Changed?.Invoke(); }
    }

    public double WeatherLatitude
    {
        get => Preferences.Default.Get(nameof(WeatherLatitude), 40.7128);
        set { Preferences.Default.Set(nameof(WeatherLatitude), value); Changed?.Invoke(); }
    }

    public double WeatherLongitude
    {
        get => Preferences.Default.Get(nameof(WeatherLongitude), -74.0060);
        set { Preferences.Default.Set(nameof(WeatherLongitude), value); Changed?.Invoke(); }
    }

    public string WeatherLocationName
    {
        get => Preferences.Default.Get(nameof(WeatherLocationName), "New York, US");
        set { Preferences.Default.Set(nameof(WeatherLocationName), value); Changed?.Invoke(); }
    }

    public bool UseFahrenheit
    {
        get => Preferences.Default.Get(nameof(UseFahrenheit), true);
        set { Preferences.Default.Set(nameof(UseFahrenheit), value); Changed?.Invoke(); }
    }

    public string? GoogleOAuthClientId
    {
        get => Preferences.Default.Get(nameof(GoogleOAuthClientId), string.Empty) is { Length: > 0 } v ? v : null;
        set { Preferences.Default.Set(nameof(GoogleOAuthClientId), value ?? string.Empty); Changed?.Invoke(); }
    }

    /// <summary>When the app last asked GitHub for a newer release. Deliberately doesn't raise Changed (that would refresh the dashboard).</summary>
    public DateTime LastUpdateCheckUtc
    {
        get => new(Preferences.Default.Get(nameof(LastUpdateCheckUtc), 0L), DateTimeKind.Utc);
        set => Preferences.Default.Set(nameof(LastUpdateCheckUtc), value.Ticks);
    }

    /// <summary>Seconds between burn-in protection nudges (pixel shift + subtle dimming pulse).</summary>
    public int BurnInIntervalSeconds
    {
        get => Preferences.Default.Get(nameof(BurnInIntervalSeconds), 45);
        set { Preferences.Default.Set(nameof(BurnInIntervalSeconds), value); Changed?.Invoke(); }
    }
}
