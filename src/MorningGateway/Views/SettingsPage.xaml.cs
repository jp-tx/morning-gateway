using MorningGateway.Models;
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
