using Android.Content;
using Android.Provider;
using MorningGateway.Services.Updates;

namespace MorningGateway;

/// <summary>Hands a downloaded APK to Android's package installer via a FileProvider URI.</summary>
public class ApkInstaller : IApkInstaller
{
    public Task<InstallResult> InstallAsync(string apkPath)
    {
        var context = Android.App.Application.Context;

        // "Install unknown apps" became a per-app permission in API 26; below that the system prompt just works.
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !context.PackageManager!.CanRequestPackageInstalls())
        {
            var settings = new Intent(Settings.ActionManageUnknownAppSources, Android.Net.Uri.Parse($"package:{context.PackageName}"));
            settings.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(settings);
            return Task.FromResult(InstallResult.NeedsPermission);
        }

        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, $"{context.PackageName}.fileprovider", new Java.IO.File(apkPath))!;
        var install = new Intent(Intent.ActionView);
        install.SetDataAndType(uri, "application/vnd.android.package-archive");
        install.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        context.StartActivity(install);
        return Task.FromResult(InstallResult.Started);
    }
}
