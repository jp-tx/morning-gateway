using CommunityToolkit.Mvvm.ComponentModel;

namespace MorningGateway.Services.Display;

/// <summary>
/// Whether the app is currently in full-screen kiosk mode (system bars hidden).
/// Deliberately session-only: after a restart or reboot the wall display comes
/// back up in kiosk mode, so "exit" can't leave a tablet permanently unlocked.
/// MainActivity watches <see cref="IsActive"/> and shows/hides the system bars.
/// </summary>
public partial class KioskService : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    bool isActive = true;

    public string ButtonText => IsActive ? "Exit kiosk mode" : "Return to kiosk mode";

    public void Toggle() => IsActive = !IsActive;
}
