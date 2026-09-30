using MorningGateway.Models;

namespace MorningGateway.Views;

public partial class WeatherSidebarView : ContentView
{
    public static readonly BindableProperty WeatherProperty =
        BindableProperty.Create(nameof(Weather), typeof(WeatherSnapshot), typeof(WeatherSidebarView));

    public WeatherSidebarView()
    {
        InitializeComponent();
    }

    public WeatherSnapshot? Weather
    {
        get => (WeatherSnapshot?)GetValue(WeatherProperty);
        set => SetValue(WeatherProperty, value);
    }
}
