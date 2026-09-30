using CommunityToolkit.Mvvm.ComponentModel;
using MorningGateway.Services.Display;

namespace MorningGateway.Services.Updates;

/// <summary>App-level update state shared by the dashboard (notice) and Settings (check / install).</summary>
public partial class UpdateCoordinator : ObservableObject
{
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    readonly UpdateService _service;
    readonly IApkInstaller _installer;
    readonly SettingsStore _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(Notice))]
    UpdateInfo? available;

    [ObservableProperty]
    string status = string.Empty;

    [ObservableProperty]
    bool isBusy;

    public UpdateCoordinator(UpdateService service, IApkInstaller installer, SettingsStore settings)
    {
        _service = service;
        _installer = installer;
        _settings = settings;
    }

    public string CurrentVersionText => $"{AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";

    public bool HasUpdate => Available is not null;

    /// <summary>Short text for the dashboard top bar; empty when up to date.</summary>
    public string Notice => Available is { } a ? $"Update {a.VersionName} available - see Settings" : string.Empty;

    static int CurrentVersionCode => int.TryParse(AppInfo.Current.BuildString, out var code) ? code : 0;

    /// <summary>Called from the dashboard's refresh timer; only actually hits the network about once a day.</summary>
    public async Task CheckIfDueAsync()
    {
        if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < CheckInterval)
        {
            return;
        }

        await CheckAsync(quiet: true);
    }

    public async Task CheckAsync(bool quiet = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!quiet)
            {
                Status = "Checking for updates...";
            }

            Available = await _service.CheckAsync(CurrentVersionCode);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            Status = Available is { } a ? $"Version {a.VersionName} is available." : "You're up to date.";
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                Status = $"Couldn't check for updates: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallAsync()
    {
        if (Available is not { } update || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Status = $"Downloading {update.VersionName}...";
            var path = Path.Combine(FileSystem.CacheDirectory, $"MorningGateway-{update.VersionName}.apk");
            await _service.DownloadAsync(update, path);

            var result = await _installer.InstallAsync(path);
            Status = result == InstallResult.NeedsPermission
                ? "Allow installs from Morning Gateway on the screen that just opened, come back, and tap Install again."
                : "Confirm the install on the system prompt.";
        }
        catch (Exception ex)
        {
            Status = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
