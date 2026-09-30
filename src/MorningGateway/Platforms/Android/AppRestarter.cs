using Android.App;
using Android.Content;
using Android.OS;

namespace MorningGateway;

/// <summary>
/// Relaunches the app in a fresh process. Used when a setting has to be applied at activity attach time
/// (e.g. the UI scale). Activity.Recreate() isn't safe here: MAUI disposes the old window's services
/// while dashboard timers are still running, which crashes. An alarm-based relaunch isn't reliable
/// either (background activity start limits), so this uses the "process phoenix" pattern: a tiny
/// activity in a separate process kills the main process and then launches the app again.
/// </summary>
public static class AppRestarter
{
    internal const string MainPidExtra = "main_pid";

    public static void Restart()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        var phoenix = new Intent(activity, typeof(RestartActivity));
        phoenix.AddFlags(ActivityFlags.NewTask);
        phoenix.PutExtra(MainPidExtra, Process.MyPid());
        activity.StartActivity(phoenix);
        activity.Finish();
    }
}

/// <summary>Runs in its own ":restart" process so it survives the main process being killed.</summary>
[Activity(Process = ":restart", Exported = false, NoHistory = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
public class RestartActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var mainPid = Intent?.GetIntExtra(AppRestarter.MainPidExtra, -1) ?? -1;
        if (mainPid > 0)
        {
            Process.KillProcess(mainPid);
        }

        var launch = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        StartActivity(launch);

        Finish();
        Java.Lang.Runtime.GetRuntime()!.Exit(0);
    }
}
