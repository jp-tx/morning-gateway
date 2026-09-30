namespace MorningGateway.Services.Display;

/// <summary>Keeps the screen from sleeping while the dashboard is the active app - essential for a wall-mounted display.</summary>
public class KeepAwakeService
{
    public void Apply(bool enabled)
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = enabled;
        }
        catch (Exception)
        {
            // Not fatal if the platform refuses (e.g. battery saver policy); the app just falls back to normal sleep behavior.
        }
    }
}
