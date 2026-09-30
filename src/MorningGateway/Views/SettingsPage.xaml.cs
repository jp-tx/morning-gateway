using MorningGateway.Models;
using MorningGateway.Services.Display;
using MorningGateway.ViewModels;

namespace MorningGateway.Views;

public partial class SettingsPage : ContentPage
{
    readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        UiScaleSlider.Value = viewModel.Settings.UiScale;
        UiScaleLabel.Text = $"{UiScaleSlider.Value:P0}";
    }

    void OnUiScaleChanged(object? sender, ValueChangedEventArgs e) =>
        UiScaleLabel.Text = $"{UiScale.Clamp(e.NewValue):P0}";

    void OnUiScaleDragCompleted(object? sender, EventArgs e) => ApplyUiScale(UiScaleSlider.Value);

    void OnUiScaleReset(object? sender, EventArgs e)
    {
        UiScaleSlider.Value = UiScale.Default;
        ApplyUiScale(UiScale.Default);
    }

    /// <summary>Saves the scale and restarts the app so the activity attaches at the new density.</summary>
    void ApplyUiScale(double value)
    {
        var scale = UiScale.Clamp(value);
        if (Math.Abs(scale - _viewModel.Settings.UiScale) < 0.001)
        {
            return;
        }

        _viewModel.Settings.UiScale = scale;
#if ANDROID
        AppRestarter.Restart();
#endif
    }

    void OnLocationResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is GeocodeResult result)
        {
            _viewModel.SelectLocationCommand.Execute(result);
            LocationResultsList.SelectedItem = null;
        }
    }

    async void OnCalDavCalendarSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is DiscoveredCalendar calendar)
        {
            await _viewModel.AddCalDavCalendarCommand.ExecuteAsync(calendar);
            CalDavResultsList.SelectedItem = null;
        }
    }

    void OnSourceEnabledToggled(object? sender, ToggledEventArgs e)
    {
        if (sender is Switch { BindingContext: CalendarSourceConfig source })
        {
            _viewModel.PersistSourceState(source);
        }
    }

    async void OnRemoveSourceClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: CalendarSourceConfig source })
        {
            await _viewModel.RemoveSourceCommand.ExecuteAsync(source);
        }
    }
}
