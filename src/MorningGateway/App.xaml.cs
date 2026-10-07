using MorningGateway.Services.Display;

namespace MorningGateway;

public partial class App : Application
{
    readonly IServiceProvider _services;

    public App(IServiceProvider services, ThemeService themeService, KeepAwakeService keepAwakeService, SettingsStore settings)
    {
        InitializeComponent();
        _services = services;

        themeService.Apply();
        // No activity exists yet at this point, so the initial keep-awake state is applied in MainActivity.OnCreate.
        settings.Changed += () => keepAwakeService.Apply(settings.KeepScreenOn);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A fresh shell (and pages) per window: Android can destroy and recreate the activity while the
        // process lives on, and pages built for the old activity must never be reused in the new one.
        return new Window(_services.GetRequiredService<AppShell>());
    }
}
