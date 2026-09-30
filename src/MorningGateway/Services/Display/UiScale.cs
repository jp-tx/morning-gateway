namespace MorningGateway.Services.Display;

/// <summary>
/// The "Text &amp; UI size" setting. Applied at the Android display-density level (see
/// MainActivity.AttachBaseContext), so text and every dp-sized element scale together
/// and layouts reflow instead of clipping.
/// </summary>
public static class UiScale
{
    public const double Min = 0.75;
    public const double Max = 1.75;
    public const double Default = 1.0;

    public static double Clamp(double scale) =>
        double.IsNaN(scale) ? Default : Math.Clamp(Math.Round(scale, 2), Min, Max);

    /// <summary>Density (dpi) to run the UI at for a given device density and scale.</summary>
    public static int ScaledDensityDpi(int deviceDpi, double scale) =>
        (int)Math.Round(deviceDpi * Clamp(scale));
}
