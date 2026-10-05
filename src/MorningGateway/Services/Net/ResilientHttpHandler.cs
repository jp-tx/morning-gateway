namespace MorningGateway.Services.Net;

/// <summary>
/// Makes HTTP calls tolerate a flaky link: a per-attempt timeout until response
/// headers arrive (so a stalled connection fails in seconds, not minutes) and a
/// few retries with backoff for idempotent requests on transient failures.
/// Large bodies (APK download) are unaffected - the timeout stops once headers are in.
/// </summary>
public class ResilientHttpHandler : DelegatingHandler
{
    static readonly HashSet<string> IdempotentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "PROPFIND", "REPORT",
    };

    readonly TimeSpan _attemptTimeout;
    readonly TimeSpan[] _retryDelays;

    public ResilientHttpHandler(HttpMessageHandler inner, TimeSpan? attemptTimeout = null, TimeSpan[]? retryDelays = null)
        : base(inner)
    {
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(20);
        _retryDelays = retryDelays ?? new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var canRetry = IdempotentMethods.Contains(request.Method.Method);
        byte[]? body = null;
        if (canRetry && request.Content is not null)
        {
            // A request can only be sent once, so keep the body to rebuild it for retries.
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 0; ; attempt++)
        {
            var current = attempt == 0 ? request : Clone(request, body);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_attemptTimeout);

            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(current, timeout.Token).ConfigureAwait(false);
                // Headers are in; don't let the attempt timer cut a long body download short.
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (!IsTransient(response) || !canRetry || attempt >= _retryDelays.Length)
                {
                    return response;
                }
            }
            catch (HttpRequestException) when (canRetry && attempt < _retryDelays.Length)
            {
                // Connection dropped or refused; fall through to back off and retry.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Our attempt timeout fired, not the caller's cancellation.
                if (!canRetry || attempt >= _retryDelays.Length)
                {
                    throw new HttpRequestException($"Request timed out after {_attemptTimeout.TotalSeconds:0}s.");
                }
            }

            response?.Dispose();
            await Task.Delay(_retryDelays[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    static bool IsTransient(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500 || response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests;

    static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in original.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
