using CommunityToolkit.Mvvm.ComponentModel;

namespace MorningGateway.Services.Display;

/// <summary>
/// OLED/LCD burn-in mitigation for an always-on dashboard: periodically nudges
/// the whole page a few pixels around a slow orbit (so no single pixel stays
/// lit in exactly the same spot for hours) and pulses a near-invisible dimming
/// overlay (so static bright regions don't sit at one fixed luminance all
/// day). The page binds its root layout's TranslationX/Y and an overlay's
/// Opacity to these properties.
/// </summary>
public partial class BurnInProtectionService : ObservableObject
{
    const double MaxOffsetDip = 9;
    const int OrbitSteps = 12;

    readonly SettingsStore _settings;
    IDispatcherTimer? _timer;
    int _step;

    [ObservableProperty]
    double offsetX;

    [ObservableProperty]
    double offsetY;

    [ObservableProperty]
    double dimOpacity;

    public BurnInProtectionService(SettingsStore settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        Stop();

        if (!_settings.BurnInProtectionEnabled)
        {
            OffsetX = 0;
            OffsetY = 0;
            DimOpacity = 0;
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(15, _settings.BurnInIntervalSeconds));
        _timer.Tick += (_, _) => Nudge();
        _timer.Start();
        Nudge();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    void Nudge()
    {
        _step++;
        var angle = (_step % OrbitSteps) * (Math.PI * 2 / OrbitSteps);
        OffsetX = Math.Round(Math.Cos(angle) * MaxOffsetDip, 1);
        OffsetY = Math.Round(Math.Sin(angle) * MaxOffsetDip, 1);
        DimOpacity = 0.02 + (0.015 * Math.Sin(_step * 0.7));
    }
}
