using Android.App;
using Android.Content;

namespace MorningGateway;

/// <summary>
/// Relaunches the dashboard automatically when the tablet reboots, so a
/// wall-mounted device recovers from a power cut without manual intervention.
/// Only useful once the app has been granted the "display over other apps /
/// launch on boot" allowance by the device's launcher settings.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, Label = "Morning Gateway Boot Receiver")]
[IntentFilter(new[] { Intent.ActionBootCompleted }, Priority = (int)IntentFilterPriority.HighPriority)]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted)
        {
            return;
        }

        var launchIntent = new Intent(context, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(launchIntent);
    }
}
