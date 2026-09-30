using System.Globalization;

namespace MorningGateway.Converters;

public class DateOnlyToWeekdayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly d ? (d == DateOnly.FromDateTime(DateTime.Today) ? "Today" : d.ToString("ddd", culture)) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class TimeOffsetToHourConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset t ? t.LocalDateTime.ToString("h tt", culture) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
