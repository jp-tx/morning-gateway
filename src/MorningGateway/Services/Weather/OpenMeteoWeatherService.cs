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

        var current = dto.Current is null ? null : new CurrentConditions(
            dto.Current.Temperature2m,
            dto.Current.ApparentTemperature,
            dto.Current.WeatherCode,
            dto.Current.IsDay == 1,
            dto.Current.WindSpeed10m);

        var hourly = new List<HourlyForecastPoint>();
        if (dto.Hourly is not null)
        {
            var now = DateTimeOffset.Now;
            for (var i = 0; i < dto.Hourly.Time.Count; i++)
            {
                var time = DateTimeOffset.Parse(dto.Hourly.Time[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                if (time < now.AddHours(-1))
                {
                    continue;
                }

                hourly.Add(new HourlyForecastPoint(
                    time,
                    dto.Hourly.Temperature2m[i],
                    dto.Hourly.PrecipitationProbability[i],
                    dto.Hourly.WeatherCode[i]));
            }
        }

        var daily = new List<DailyForecastPoint>();
        if (dto.Daily is not null)
        {
            for (var i = 0; i < dto.Daily.Time.Count; i++)
            {
                daily.Add(new DailyForecastPoint(
                    DateOnly.Parse(dto.Daily.Time[i], CultureInfo.InvariantCulture),
                    dto.Daily.Temperature2mMax[i],
                    dto.Daily.Temperature2mMin[i],
                    dto.Daily.PrecipitationProbabilityMax[i],
                    dto.Daily.WeatherCode[i]));
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

    // --- Open-Meteo response DTOs ---

    class ForecastResponse
    {
        [JsonPropertyName("current")] public CurrentDto? Current { get; set; }
        [JsonPropertyName("hourly")] public HourlyDto? Hourly { get; set; }
        [JsonPropertyName("daily")] public DailyDto? Daily { get; set; }
    }

    class CurrentDto
    {
        [JsonPropertyName("temperature_2m")] public double Temperature2m { get; set; }
        [JsonPropertyName("apparent_temperature")] public double ApparentTemperature { get; set; }
        [JsonPropertyName("weather_code")] public int WeatherCode { get; set; }
        [JsonPropertyName("is_day")] public int IsDay { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double WindSpeed10m { get; set; }
    }

    class HourlyDto
    {
        [JsonPropertyName("time")] public List<string> Time { get; set; } = new();
        [JsonPropertyName("temperature_2m")] public List<double> Temperature2m { get; set; } = new();
        [JsonPropertyName("precipitation_probability")] public List<int> PrecipitationProbability { get; set; } = new();
        [JsonPropertyName("weather_code")] public List<int> WeatherCode { get; set; } = new();
    }

    class DailyDto
    {
        [JsonPropertyName("time")] public List<string> Time { get; set; } = new();
        [JsonPropertyName("temperature_2m_max")] public List<double> Temperature2mMax { get; set; } = new();
        [JsonPropertyName("temperature_2m_min")] public List<double> Temperature2mMin { get; set; } = new();
        [JsonPropertyName("precipitation_probability_max")] public List<int> PrecipitationProbabilityMax { get; set; } = new();
        [JsonPropertyName("weather_code")] public List<int> WeatherCode { get; set; } = new();
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
