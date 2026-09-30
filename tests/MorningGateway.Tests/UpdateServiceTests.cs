using System.Security.Cryptography;
using System.Text;
using MorningGateway.Services.Updates;

namespace MorningGateway.Tests;

// Auto-update: the release workflow publishes latest.json + a signed APK; the app compares version codes,
// downloads over https and verifies the sha256 before handing the file to Android's installer.
public class UpdateServiceTests : IDisposable
{
    static readonly byte[] Apk = Encoding.UTF8.GetBytes("pretend this is an apk");
    static readonly string ApkHash = Convert.ToHexString(SHA256.HashData(Apk)).ToLowerInvariant();
    readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static string Manifest(int code = 5, string url = "https://github.com/x/y/releases/download/v1/app.apk", string? sha = null) =>
        $$"""{"versionCode":{{code}},"versionName":"1.{{code}}.0","apkUrl":"{{url}}","sha256":"{{sha ?? ApkHash}}","notes":"n"}""";

    static UpdateService Service(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHandler((r, _) => respond(r)).Client(), "https://example.test/latest.json");

    [Fact]
    public async Task Newer_release_is_offered()
    {
        var info = await Service(_ => StubHandler.Json(Manifest(code: 5))).CheckAsync(currentVersionCode: 4);
        Assert.Equal("1.5.0", info!.VersionName);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public async Task Same_or_older_release_is_not_offered(int installed) =>
        Assert.Null(await Service(_ => StubHandler.Json(Manifest(code: 5))).CheckAsync(installed));

    [Fact]
    public async Task Manifest_with_a_plain_http_apk_url_is_rejected() =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Service(_ => StubHandler.Json(Manifest(url: "http://example.com/app.apk"))).CheckAsync(1));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task Manifest_without_a_valid_sha256_is_rejected(string sha) =>
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Service(_ => StubHandler.Json(Manifest(sha: sha))).CheckAsync(1));

    [Fact]
    public async Task Server_error_while_checking_throws() =>
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Service(_ => StubHandler.Text("no", System.Net.HttpStatusCode.NotFound)).CheckAsync(1));

    [Fact]
    public async Task Download_with_matching_hash_is_saved()
    {
        var svc = Service(_ => new HttpResponseMessage { Content = new ByteArrayContent(Apk) });
        var dest = Path.Combine(_dir, "app.apk");
        await svc.DownloadAsync(new UpdateInfo(5, "1.5.0", "https://x/app.apk", ApkHash.ToUpperInvariant()), dest);

        Assert.Equal(Apk, await File.ReadAllBytesAsync(dest));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task Download_with_wrong_hash_leaves_nothing_behind()
    {
        var svc = Service(_ => new HttpResponseMessage { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")) });
        var dest = Path.Combine(_dir, "app.apk");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            svc.DownloadAsync(new UpdateInfo(5, "1.5.0", "https://x/app.apk", ApkHash), dest));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
