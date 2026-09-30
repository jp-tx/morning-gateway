using System.Globalization;
using MorningGateway.Models;
using MorningGateway.Services.Weather;

namespace MorningGateway.Converters;

/// <summary>Plain WMO weather code (as used in hourly/daily forecast rows, always shown as a daytime icon).</summary>
public class WeatherCodeGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int code ? WeatherCodeInfo.Glyph(code, isDay: true) : "☁️";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Current conditions, which carries its own day/night flag.</summary>
public class CurrentConditionsGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CurrentConditions c ? WeatherCodeInfo.Glyph(c.WeatherCode, c.IsDay) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
