using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace MorningGateway.Services.Updates;

/// <summary>One published release, as described by latest.json on the GitHub release.</summary>
public record UpdateInfo(
    [property: JsonPropertyName("versionCode")] int VersionCode,
    [property: JsonPropertyName("versionName")] string VersionName,
    [property: JsonPropertyName("apkUrl")] string ApkUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("notes")] string? Notes = null);

public enum InstallResult
{
    /// <summary>The system installer was launched; the user confirms there.</summary>
    Started,

    /// <summary>"Install unknown apps" isn't allowed for this app yet; the settings screen was opened.</summary>
    NeedsPermission,
}

public interface IApkInstaller
{
    Task<InstallResult> InstallAsync(string apkPath);
}

/// <summary>
/// Checks GitHub Releases for a newer build and downloads it. The release
/// workflow publishes a latest.json next to the signed APK; Android itself
/// refuses to install an APK not signed with the same key, so this only has to
/// make sure the download is intact (sha256) and comes over https.
/// </summary>
public class UpdateService
{
    public const string DefaultLatestUrl = "https://github.com/jp-tx/morning-gateway/releases/latest/download/latest.json";

    readonly HttpClient _http;
    readonly string _latestUrl;

    public UpdateService(HttpClient http, string latestUrl = DefaultLatestUrl)
    {
        _http = http;
        _latestUrl = latestUrl;
    }

    /// <summary>Returns the latest release if it is newer than <paramref name="currentVersionCode"/>, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(int currentVersionCode, CancellationToken cancellationToken = default)
    {
        var info = await _http.GetFromJsonAsync<UpdateInfo>(_latestUrl, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Empty update manifest.");

        if (!Uri.TryCreate(info.ApkUrl, UriKind.Absolute, out var apk) || apk.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Update manifest has a non-https APK URL.");
        }

        if (info.Sha256 is not { Length: 64 } || !info.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Update manifest has no valid sha256.");
        }

        return info.VersionCode > currentVersionCode ? info : null;
    }

    /// <summary>Downloads the APK to <paramref name="destinationPath"/>; deletes it and throws if the hash doesn't match.</summary>
    public async Task DownloadAsync(UpdateInfo info, string destinationPath, CancellationToken cancellationToken = default)
    {
        var partial = destinationPath + ".part";
        try
        {
            using var response = await _http.GetAsync(info.ApkUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string hash;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = Convert.ToHexString(sha.Hash!);
            }

            if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded update failed its integrity check.");
            }

            File.Move(partial, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }
}
