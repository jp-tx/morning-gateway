using Microsoft.Extensions.Logging;
using MorningGateway.Services.Calendar;
using MorningGateway.Services.Display;
using MorningGateway.Services.Weather;
using MorningGateway.ViewModels;
using MorningGateway.Views;

namespace MorningGateway;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // --- Cross-cutting ---
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<SettingsStore>();

        // --- Calendar ---
        builder.Services.AddSingleton<CalendarSourceStore>();
        builder.Services.AddSingleton<CalDavClient>();
        builder.Services.AddSingleton<GoogleCalendarProvider>();
        builder.Services.AddSingleton<ICalendarProvider>(sp => sp.GetRequiredService<GoogleCalendarProvider>());
        builder.Services.AddSingleton<ICalendarProvider, IcsUrlCalendarProvider>();
        builder.Services.AddSingleton<ICalendarProvider, CalDavCalendarProvider>();
        builder.Services.AddSingleton<CalendarAggregatorService>();

        // --- Weather ---
        builder.Services.AddSingleton<IWeatherService, OpenMeteoWeatherService>();
        builder.Services.AddSingleton<DeviceLocationService>();

        // --- Display / kiosk behavior ---
        builder.Services.AddSingleton<KeepAwakeService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<BurnInProtectionService>();

        // --- App shell, pages, view models ---
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
