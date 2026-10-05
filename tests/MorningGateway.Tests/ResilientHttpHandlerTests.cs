using System.Net;
using MorningGateway.Services.Net;

namespace MorningGateway.Tests;

public class ResilientHttpHandlerTests
{
    static readonly TimeSpan[] NoDelay = { TimeSpan.Zero, TimeSpan.Zero };

    delegate Task<HttpResponseMessage> Step(HttpRequestMessage request, CancellationToken ct);

    class Scripted : HttpMessageHandler
    {
        readonly Queue<Step> _steps;
        public int Calls;
        public List<string> Bodies { get; } = new();
        public Scripted(params Step[] steps) => _steps = new(steps);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await _steps.Dequeue()(request, cancellationToken);
        }
    }

    static Step Ok() => (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    static Step Drop() => (_, _) => throw new HttpRequestException("connection reset");

    static Step Status(HttpStatusCode code) => (_, _) => Task.FromResult(new HttpResponseMessage(code));

    /// <summary>A connection that never answers; only the cancellation token ends it.</summary>
    static Step Stall() => async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new HttpResponseMessage(HttpStatusCode.OK);
    };

    static HttpClient Client(HttpMessageHandler inner, TimeSpan? timeout = null) =>
        new(new ResilientHttpHandler(inner, timeout, NoDelay));

    [Fact]
    public async Task Get_is_retried_after_a_dropped_connection()
    {
        var inner = new Scripted(Drop(), Drop(), Ok());
        using var response = await Client(inner).GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Server_errors_are_retried_and_the_last_response_is_returned_when_they_persist()
    {
        var inner = new Scripted(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.ServiceUnavailable));
        using var response = await Client(inner).GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var inner = new Scripted(Status(HttpStatusCode.NotFound));
        using var response = await Client(inner).GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Persistent_connection_failure_surfaces_as_HttpRequestException()
    {
        var inner = new Scripted(Drop(), Drop(), Drop());
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(inner).GetAsync("http://x/"));
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Post_is_never_retried()
    {
        var inner = new Scripted(Drop(), Ok());
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(inner).PostAsync("http://x/", new StringContent("a=b")));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_stalled_attempt_times_out_and_is_retried()
    {
        var inner = new Scripted(Stall(), Ok());
        using var response = await Client(inner, TimeSpan.FromMilliseconds(100)).GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task A_stall_that_never_recovers_becomes_HttpRequestException_not_a_cancellation()
    {
        var inner = new Scripted(Stall(), Stall(), Stall());
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(inner, TimeSpan.FromMilliseconds(50)).GetAsync("http://x/"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var inner = new Scripted(Ok());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(inner).GetAsync("http://x/", cts.Token));
        Assert.True(inner.Calls <= 1, "a cancelled request must not be retried");
    }

    [Fact]
    public async Task Idempotent_requests_with_a_body_resend_the_body_on_retry()
    {
        var inner = new Scripted(Drop(), Ok());
        var request = new HttpRequestMessage(new HttpMethod("REPORT"), "http://x/") { Content = new StringContent("<q/>") };
        request.Headers.TryAddWithoutValidation("Depth", "1");
        using var response = await Client(inner).SendAsync(request);
        Assert.Equal(new[] { "<q/>", "<q/>" }, inner.Bodies);
    }
}
