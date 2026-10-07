using Android.App;
using Android.Runtime;
using MorningGateway.Services.Diagnostics;

namespace MorningGateway;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        // Last line of defence: a stray exception from a background task or async void handler
        // (typically a network call failing at a bad moment) must not kill an always-on dashboard.
        // Everything that lands here is written to crash.log so it can be diagnosed later.
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            CrashLog.Write("Unhandled exception (swallowed)", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("Fatal unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public override void OnCreate()
    {
        try
        {
            // App-specific external storage: readable over USB / adb without root, no permission needed.
            var directory = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir?.AbsolutePath;
            if (directory is not null)
            {
                CrashLog.FilePath = Path.Combine(directory, "crash.log");
            }
        }
        catch (Exception)
        {
            // Without a log path, CrashLog still writes to logcat.
        }

        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
