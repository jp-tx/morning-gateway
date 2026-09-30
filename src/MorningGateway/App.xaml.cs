using MorningGateway.Services.Display;

namespace MorningGateway;

public partial class App : Application
{
    readonly AppShell _shell;

    public App(AppShell shell, ThemeService themeService, KeepAwakeService keepAwakeService, SettingsStore settings)
    {
        InitializeComponent();
        _shell = shell;

        themeService.Apply();
        keepAwakeService.Apply(settings.KeepScreenOn);
        settings.Changed += () => keepAwakeService.Apply(settings.KeepScreenOn);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_shell);
    }
}
