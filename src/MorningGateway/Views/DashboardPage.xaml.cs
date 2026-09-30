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
}
