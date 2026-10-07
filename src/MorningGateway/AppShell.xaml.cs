using MorningGateway.Views;

namespace MorningGateway;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
    }

    protected override bool OnBackButtonPressed()
    {
        // Back from Settings works as usual; back on the dashboard itself must not close a wall display.
        if (Navigation.NavigationStack.Count > 1)
        {
            return base.OnBackButtonPressed();
        }

        return true;
    }
}
