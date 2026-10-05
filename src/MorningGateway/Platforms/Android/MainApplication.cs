using Android.App;
using Android.Runtime;

namespace MorningGateway;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        // Last line of defence: a stray exception from a background task or async void handler
        // (typically a network call failing at a bad moment) must not kill an always-on dashboard.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception swallowed: {e.Exception}");
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
