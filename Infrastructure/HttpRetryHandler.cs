using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace SafetyCultureSync.Infrastructure;

/// <summary>
/// Delegating handler für automatisches Retry bei 429 / 5xx.
/// Respektiert den Retry-After-Header von SafetyCulture.
/// </summary>
public sealed class HttpRetryHandler : DelegatingHandler
{
    private readonly ILogger<HttpRetryHandler> _logger;

    private static readonly IAsyncPolicy<HttpResponseMessage> RetryPolicy =
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    // Retry-After-Header vorrangig nutzen
                    if (outcome.Result?.Headers.RetryAfter?.Delta is { } delta)
                        return delta;

                    // Ansonsten: exponentielles Backoff (2s, 4s, 8s)
                    return TimeSpan.FromSeconds(Math.Pow(2, attempt));
                },
                onRetryAsync: (outcome, delay, attempt, _) =>
                {
                    // Logging erfolgt im Caller — hier nur fire-and-forget
                    return Task.CompletedTask;
                });

    public HttpRetryHandler(ILogger<HttpRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await RetryPolicy.ExecuteAsync(async () =>
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _logger.LogWarning(
                    "Rate-Limit erreicht (429). Warte {Seconds}s vor Retry...",
                    retryAfter.TotalSeconds);
            }

            return response;
        });
    }
}
