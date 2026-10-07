using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MorningGateway.Models;

namespace MorningGateway.Services.Weather;

/// <summary>
/// Talks to Open-Meteo (https://open-meteo.com) - free, keyless weather and
/// geocoding APIs, well suited to an always-on dashboard that polls
/// infrequently and doesn't want an API-key management story.
/// </summary>
public class OpenMeteoWeatherService : IWeatherService
{
    readonly HttpClient _http;

    public OpenMeteoWeatherService(HttpClient http)
    {
        _http = http;
    }

    public async Task<WeatherSnapshot> GetForecastAsync(double latitude, double longitude, string locationName, bool useFahrenheit, CancellationToken cancellationToken = default)
    {
        var unit = useFahrenheit ? "fahrenheit" : "celsius";
        var url = "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}"
            + $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}"
            + "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m"
            + "&hourly=temperature_2m,precipitation_probability,weather_code"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + $"&temperature_unit={unit}"
            + "&wind_speed_unit=kmh&timezone=auto&forecast_days=7";

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<ForecastResponse>(stream, JsonOpts, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty forecast response from Open-Meteo.");

        var current = dto.Current?.Temperature2m is not { } currentTemperature ? null : new CurrentConditions(
            currentTemperature,
            dto.Current.ApparentTemperature ?? currentTemperature,
            (int)(dto.Current.WeatherCode ?? 3),
            dto.Current.IsDay != 0,
            dto.Current.WindSpeed10m ?? 0);

        // Open-Meteo sends null for values it has no data for and doesn't promise equal-length arrays,
        // so every lookup is tolerant: one missing number must not cost the whole forecast.
        var hourly = new List<HourlyForecastPoint>();
        if (dto.Hourly?.Time is { } hourlyTimes)
        {
            var now = DateTimeOffset.Now;
            for (var i = 0; i < hourlyTimes.Count; i++)
            {
                if (ParseForecastTime(hourlyTimes[i], dto.UtcOffsetSeconds) is not { } time || time < now.AddHours(-1) || At(dto.Hourly.Temperature2m, i) is not { } temperature)
                {
                    continue;
                }

                hourly.Add(new HourlyForecastPoint(
                    time,
                    temperature,
                    (int)Math.Round(At(dto.Hourly.PrecipitationProbability, i) ?? 0),
                    (int)(At(dto.Hourly.WeatherCode, i) ?? 3)));
            }
        }

        var daily = new List<DailyForecastPoint>();
        if (dto.Daily?.Time is { } dailyTimes)
        {
            for (var i = 0; i < dailyTimes.Count; i++)
            {
                if (!DateOnly.TryParse(dailyTimes[i], CultureInfo.InvariantCulture, out var date)
                    || At(dto.Daily.Temperature2mMax, i) is not { } max || At(dto.Daily.Temperature2mMin, i) is not { } min)
                {
                    continue;
                }

                daily.Add(new DailyForecastPoint(
                    date,
                    max,
                    min,
                    (int)Math.Round(At(dto.Daily.PrecipitationProbabilityMax, i) ?? 0),
                    (int)(At(dto.Daily.WeatherCode, i) ?? 3)));
            }
        }

        return new WeatherSnapshot
        {
            LocationName = locationName,
            UnitSuffix = useFahrenheit ? "°F" : "°C",
            Current = current,
            Hourly = hourly.Take(12).ToList(),
            Daily = daily,
        };
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchLocationAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<GeocodeResult>();
        }

        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=8&language=en&format=json";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<GeocodeResponse>(stream, JsonOpts, cancellationToken).ConfigureAwait(false);

        return dto?.Results?.Select(r => new GeocodeResult(r.Name, r.Admin1 ?? string.Empty, r.Country ?? string.Empty, r.Latitude, r.Longitude)).ToList()
            ?? new List<GeocodeResult>();
    }

    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    static double? At(List<double?>? values, int index) =>
        values is not null && index < values.Count ? values[index] : null;

    /// <summary>
    /// Forecast times come back as wall-clock time at the forecast location with no offset; the response's
    /// utc_offset_seconds says what that offset is. Without it, fall back to the device's time zone.
    /// </summary>
    static DateTimeOffset? ParseForecastTime(string? text, int? utcOffsetSeconds)
    {
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock))
        {
            return null;
        }

        try
        {
            return utcOffsetSeconds is { } seconds
                ? new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.FromSeconds(seconds))
                : new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Local));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // --- Open-Meteo response DTOs ---
    // Numbers are read as nullable doubles throughout: a single null ("no data") or fractional value
    // would otherwise fail the entire deserialization against a List<int>.

    class ForecastResponse
    {
        [JsonPropertyName("utc_offset_seconds")] public int? UtcOffsetSeconds { get; set; }
        [JsonPropertyName("current")] public CurrentDto? Current { get; set; }
        [JsonPropertyName("hourly")] public HourlyDto? Hourly { get; set; }
        [JsonPropertyName("daily")] public DailyDto? Daily { get; set; }
    }

    class CurrentDto
    {
        [JsonPropertyName("temperature_2m")] public double? Temperature2m { get; set; }
        [JsonPropertyName("apparent_temperature")] public double? ApparentTemperature { get; set; }
        [JsonPropertyName("weather_code")] public double? WeatherCode { get; set; }
        [JsonPropertyName("is_day")] public double? IsDay { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double? WindSpeed10m { get; set; }
    }

    class HourlyDto
    {
        [JsonPropertyName("time")] public List<string?>? Time { get; set; }
        [JsonPropertyName("temperature_2m")] public List<double?>? Temperature2m { get; set; }
        [JsonPropertyName("precipitation_probability")] public List<double?>? PrecipitationProbability { get; set; }
        [JsonPropertyName("weather_code")] public List<double?>? WeatherCode { get; set; }
    }

    class DailyDto
    {
        [JsonPropertyName("time")] public List<string?>? Time { get; set; }
        [JsonPropertyName("temperature_2m_max")] public List<double?>? Temperature2mMax { get; set; }
        [JsonPropertyName("temperature_2m_min")] public List<double?>? Temperature2mMin { get; set; }
        [JsonPropertyName("precipitation_probability_max")] public List<double?>? PrecipitationProbabilityMax { get; set; }
        [JsonPropertyName("weather_code")] public List<double?>? WeatherCode { get; set; }
    }

    class GeocodeResponse
    {
        [JsonPropertyName("results")] public List<GeocodeResultDto>? Results { get; set; }
    }

    class GeocodeResultDto
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
    }
}
