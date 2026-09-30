using System.Net;
using System.Text;

namespace MorningGateway.Tests;

/// <summary>Records every request and answers from a callback - no network in tests.</summary>
public class StubHandler : HttpMessageHandler
{
    readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();

    public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return _respond(request, body);
    }

    public HttpClient Client() => new(this);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
}

/// <summary>
/// Preferences/SecureStorage/WebAuthenticator shims are process-wide statics, so
/// every test class that touches them shares this collection (serial) and starts clean.
/// </summary>
[CollectionDefinition("MauiStatics", DisableParallelization = true)]
public class MauiStaticsCollection { }

public abstract class MauiStaticsTestBase : IDisposable
{
    protected MauiStaticsTestBase()
    {
        Preferences.Default.Clear();
        SecureStorage.Default.RemoveAll();
        WebAuthenticator.Default.Handler = null;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
