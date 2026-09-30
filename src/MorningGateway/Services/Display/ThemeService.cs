namespace MorningGateway.Services.Display;

/// <summary>Applies the user's Light/Dark/System choice to the whole app.</summary>
public class ThemeService
{
    readonly SettingsStore _settings;

    public ThemeService(SettingsStore settings)
    {
        _settings = settings;
        _settings.Changed += Apply;
    }

    public void Apply()
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.UserAppTheme = _settings.ThemeMode switch
        {
            AppThemeMode.Light => AppTheme.Light,
            AppThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }
}
