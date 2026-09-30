namespace MorningGateway.Models;

public record CurrentConditions(double Temperature, double FeelsLike, int WeatherCode, bool IsDay, double WindKph);

public record HourlyForecastPoint(DateTimeOffset Time, double Temperature, int PrecipitationProbability, int WeatherCode);

public record DailyForecastPoint(DateOnly Date, double TempMax, double TempMin, int PrecipitationProbability, int WeatherCode);

public class WeatherSnapshot
{
    public string LocationName { get; init; } = string.Empty;
    public string UnitSuffix { get; init; } = "°";
    public CurrentConditions? Current { get; init; }
    public IReadOnlyList<HourlyForecastPoint> Hourly { get; init; } = Array.Empty<HourlyForecastPoint>();
    public IReadOnlyList<DailyForecastPoint> Daily { get; init; } = Array.Empty<DailyForecastPoint>();
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.Now;
}

public record GeocodeResult(string Name, string Region, string Country, double Latitude, double Longitude);
