namespace MorningGateway.Services.Weather;

/// <summary>
/// Thin wrapper over MAUI's Geolocation API. The dashboard normally runs off a
/// manually-configured location (set once in Settings), but this lets that
/// field be pre-filled with the device's current position.
/// </summary>
public class DeviceLocationService
{
    public async Task<Location?> TryGetCurrentLocationAsync()
    {
        try
        {
            var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                return null;
            }

            var request = new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(10));
            return await Geolocation.Default.GetLocationAsync(request);
        }
        catch (Exception)
        {
            // Location is a convenience for pre-filling settings, never required.
            return null;
        }
    }
}
