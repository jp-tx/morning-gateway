using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using MorningGateway.Services.Display;

namespace MorningGateway;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = Android.Content.PM.LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.ScreenSize | Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.UiMode
        | Android.Content.PM.ConfigChanges.ScreenLayout | Android.Content.PM.ConfigChanges.SmallestScreenSize | Android.Content.PM.ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    KioskService? _kiosk;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _kiosk = IPlatformApplication.Current?.Services.GetService<KioskService>();
        if (_kiosk is not null)
        {
            _kiosk.PropertyChanged += (_, _) => RunOnUiThread(ApplyKioskState);
        }

        ApplyKioskState();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplyKioskState();
        }
    }

    void ApplyKioskState()
    {
        if (_kiosk?.IsActive ?? true)
        {
            HideSystemBars();
        }
        else
        {
            ShowSystemBars();
        }
    }

    /// <summary>Leaves kiosk presentation: brings the status and navigation bars back.</summary>
    void ShowSystemBars()
    {
        if (Window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Window.SetDecorFitsSystemWindows(true);
            Window.InsetsController?.Show(WindowInsets.Type.SystemBars());
        }
        else
        {
#pragma warning disable CA1422
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
#pragma warning restore CA1422
        }
    }

    /// <summary>
    /// Full-screen "kiosk" presentation for a wall-mounted display: hides the status
    /// and navigation bars and lets the user swipe them back in briefly if needed.
    /// </summary>
    void HideSystemBars()
    {
        if (Window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            Window.SetDecorFitsSystemWindows(false);
            var controller = Window.InsetsController;
            if (controller is not null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }
        else
        {
#pragma warning disable CA1422
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.ImmersiveSticky
                | SystemUiFlags.LayoutStable
                | SystemUiFlags.LayoutHideNavigation
                | SystemUiFlags.LayoutFullscreen
                | SystemUiFlags.HideNavigation
                | SystemUiFlags.Fullscreen);
#pragma warning restore CA1422
        }
    }
}
