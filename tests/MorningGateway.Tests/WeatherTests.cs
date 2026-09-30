using MorningGateway.Services.Weather;

namespace MorningGateway.Tests;

// README: "current conditions, next 12 hours, 7-day forecast, via Open-Meteo (free, no API key)".
public class WeatherTests
{
    static string Forecast(int hours = 30, int days = 7)
    {
        var start = DateTime.Now.Date.AddDays(-1);   // includes past hours the service must drop
        var times = string.Join(",", Enumerable.Range(0, hours).Select(i => $"\"{start.AddHours(i):yyyy-MM-ddTHH:mm}\""));
        var zeros = string.Join(",", Enumerable.Repeat("1", hours));
        var dates = string.Join(",", Enumerable.Range(0, days).Select(i => $"\"{DateTime.Today.AddDays(i):yyyy-MM-dd}\""));
        var dz = string.Join(",", Enumerable.Repeat("2", days));
        return $$$"""
        {"current":{"temperature_2m":71.5,"apparent_temperature":70.1,"weather_code":2,"is_day":1,"wind_speed_10m":12.3},
         "hourly":{"time":[{{{times}}}],"temperature_2m":[{{{zeros}}}],"precipitation_probability":[{{{zeros}}}],"weather_code":[{{{zeros}}}]},
         "daily":{"time":[{{{dates}}}],"temperature_2m_max":[{{{dz}}}],"temperature_2m_min":[{{{dz}}}],"precipitation_probability_max":[{{{dz}}}],"weather_code":[{{{dz}}}]}}
        """;
    }

    [Fact]
    public async Task Forecast_maps_current_conditions_seven_days_and_at_most_twelve_future_hours()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(Forecast(hours: 72)));
        var snap = await new OpenMeteoWeatherService(handler.Client()).GetForecastAsync(40.7, -74.0, "NYC", useFahrenheit: true);

        Assert.Equal("NYC", snap.LocationName);
        Assert.Equal("°F", snap.UnitSuffix);
        Assert.Equal(71.5, snap.Current!.Temperature);
        Assert.True(snap.Current.IsDay);
        Assert.Equal(7, snap.Daily.Count);
        Assert.Equal(12, snap.Hourly.Count);
        Assert.All(snap.Hourly, h => Assert.True(h.Time >= DateTimeOffset.Now.AddHours(-1.01)));
    }

    [Theory]
    [InlineData(true, "fahrenheit", "°F")]
    [InlineData(false, "celsius", "°C")]
    public async Task Unit_preference_is_sent_and_reflected(bool f, string param, string suffix)
    {
        var handler = new StubHandler((_, _) => StubHandler.Json(Forecast()));
        var snap = await new OpenMeteoWeatherService(handler.Client()).GetForecastAsync(1, 2, "x", f);

        Assert.Equal(suffix, snap.UnitSuffix);
        Assert.Contains($"temperature_unit={param}", handler.Calls.Single().Request.RequestUri!.Query);
    }

    [Fact]
    public async Task Coordinates_are_sent_with_invariant_formatting()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var handler = new StubHandler((_, _) => StubHandler.Json(Forecast()));
            await new OpenMeteoWeatherService(handler.Client()).GetForecastAsync(40.7128, -74.006, "x", true);
            var q = handler.Calls.Single().Request.RequestUri!.Query;
            Assert.Contains("latitude=40.7128", q);
            Assert.Contains("longitude=-74.006", q);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public async Task Search_by_city_name_returns_results_and_skips_the_network_for_blank_queries()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("""{"results":[{"name":"Paris","admin1":"Ile-de-France","country":"France","latitude":48.85,"longitude":2.35},{"name":"Paris","latitude":33.6,"longitude":-95.5}]}"""));
        var svc = new OpenMeteoWeatherService(handler.Client());

        var results = await svc.SearchLocationAsync("Paris");
        Assert.Equal(2, results.Count);
        Assert.Equal("France", results[0].Country);
        Assert.Equal(string.Empty, results[1].Region);

        Assert.Empty(await svc.SearchLocationAsync("   "));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Search_with_no_matches_returns_empty()
    {
        var handler = new StubHandler((_, _) => StubHandler.Json("{}"));
        Assert.Empty(await new OpenMeteoWeatherService(handler.Client()).SearchLocationAsync("zzzz"));
    }

    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(45, "Fog")]
    [InlineData(65, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(1234, "Unknown")]
    public void Weather_codes_have_labels(int code, string label) => Assert.Equal(label, WeatherCodeInfo.Describe(code));

    [Fact]
    public void Clear_sky_glyph_differs_between_day_and_night() =>
        Assert.NotEqual(WeatherCodeInfo.Glyph(0, true), WeatherCodeInfo.Glyph(0, false));
}
