using MorningGateway.Models;

namespace MorningGateway.Services.Weather;

public interface IWeatherService
{
    Task<WeatherSnapshot> GetForecastAsync(double latitude, double longitude, string locationName, bool useFahrenheit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeocodeResult>> SearchLocationAsync(string query, CancellationToken cancellationToken = default);
}
