using MorningGateway.ViewModels;

namespace MorningGateway.Views;

public partial class DashboardPage : ContentPage
{
    readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartClocksAndTimers();
        _ = _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        // Also runs when the activity is destroyed, which is what stops the timers driving a dead page.
        _viewModel.Stop();
        base.OnDisappearing();
    }
}
