namespace MorningGateway.Services.Weather;

/// <summary>
/// Maps Open-Meteo's WMO weather codes (see https://open-meteo.com/en/docs) to a
/// short label and a Segoe MDL2 / Fluent-style glyph rendered via the app's icon font.
/// </summary>
public static class WeatherCodeInfo
{
    public static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        77 => "Snow grains",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm w/ hail",
        _ => "Unknown",
    };

    /// <summary>
    /// Plain emoji rather than a custom icon font: Android ships NotoColorEmoji
    /// out of the box, so this renders correctly with zero bundled assets.
    /// </summary>
    public static string Glyph(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "☀️" : "\U0001F319",
        2 => isDay ? "⛅" : "☁️",
        3 => "☁️",
        45 or 48 => "\U0001F32B️",
        51 or 53 or 55 or 56 or 57 => "\U0001F326️",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "\U0001F327️",
        71 or 73 or 75 or 77 or 85 or 86 => "\U0001F328️",
        95 or 96 or 99 => "⛈️",
        _ => "☁️",
    };
}
