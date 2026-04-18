using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BookSlot.Domain.Webhooks;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Delivers pending and retryable <see cref="WebhookDelivery"/> rows over HTTP.
/// Each payload is signed with HMAC-SHA256 using the endpoint's secret; the
/// signature is transported in the <c>X-BookSlot-Signature</c> header along with
/// a matching <c>X-BookSlot-Timestamp</c> header so subscribers can guard against
/// replay. Failed attempts follow a fixed exponential-backoff ladder
/// (1m → 5m → 30m → 2h → 8h); after <see cref="MaxAttempts"/> attempts the
/// delivery is moved to the <see cref="WebhookDeliveryStatus.Exhausted"/>
/// dead-letter state and no further attempts are made.
/// </summary>
internal sealed class WebhookDeliveryJob : IWorkerJob
{
    /// <summary>Attempts after which a delivery is parked in <see cref="WebhookDeliveryStatus.Exhausted"/>.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] BackoffLadder =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(8),
    };

    private const int BatchSize = 50;
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<WebhookDeliveryJob> _logger;

    public WebhookDeliveryJob(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        TimeProvider clock,
        ILogger<WebhookDeliveryJob> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "webhook-delivery";

    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var due = await _db.WebhookDeliveries
            .IgnoreQueryFilters()
            .Where(d => (d.Status == WebhookDeliveryStatus.Pending || d.Status == WebhookDeliveryStatus.Failed)
                        && d.NextAttemptAt != null && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0) return;

        var endpointIds = due.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = await _db.WebhookEndpoints
            .IgnoreQueryFilters()
            .Where(e => endpointIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var delivery in due)
        {
            if (!endpoints.TryGetValue(delivery.EndpointId, out var endpoint) || !endpoint.IsActive)
            {
                delivery.MarkExhausted(null, "Endpoint missing or deactivated", _clock.GetUtcNow());
                continue;
            }

            delivery.MarkInFlight(_clock.GetUtcNow());
            try
            {
                await AttemptAsync(delivery, endpoint, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // transport exceptions are captured as failed attempts for retry
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScheduleFailure(delivery, statusCode: null, snippet: ex.Message);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AttemptAsync(
        WebhookDelivery delivery,
        WebhookEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("webhook-delivery");
        client.Timeout = HttpTimeout;

        var now = _clock.GetUtcNow();
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = ComputeSignature(endpoint.Secret, timestamp, delivery.Payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-BookSlot-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-BookSlot-Delivery", delivery.Id.ToString("N"));
        request.Headers.TryAddWithoutValidation("X-BookSlot-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-BookSlot-Signature", "sha256=" + signature);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        string snippet;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            snippet = body.Length <= 512 ? body : body[..512];
        }
#pragma warning disable CA1031
        catch (Exception ex) { snippet = "<body read failed: " + ex.Message + ">"; }
#pragma warning restore CA1031

        if (response.IsSuccessStatusCode)
        {
            delivery.MarkSucceeded((int)response.StatusCode, snippet, _clock.GetUtcNow());
            _logger.LogInformation("Webhook {Delivery} → {Url} succeeded ({Status}).",
                delivery.Id, endpoint.Url, (int)response.StatusCode);
        }
        else
        {
            ScheduleFailure(delivery, (int)response.StatusCode, snippet);
        }
    }

    private void ScheduleFailure(WebhookDelivery delivery, int? statusCode, string? snippet)
    {
        var now = _clock.GetUtcNow();
        if (delivery.AttemptCount >= MaxAttempts)
        {
            delivery.MarkExhausted(statusCode, snippet, now);
            _logger.LogWarning("Webhook delivery {Delivery} exhausted after {Attempts} attempts.",
                delivery.Id, delivery.AttemptCount);
            return;
        }

        // Attempt N just completed → index into ladder for the N-th wait (0-based).
        var ladderIndex = Math.Min(delivery.AttemptCount - 1, BackoffLadder.Length - 1);
        var delay = BackoffLadder[Math.Max(0, ladderIndex)];
        delivery.MarkFailed(statusCode, snippet, nextAttemptAt: now + delay, now);
        _logger.LogInformation("Webhook delivery {Delivery} failed (status={Status}, attempt={Attempt}), next in {Delay}.",
            delivery.Id, statusCode, delivery.AttemptCount, delay);
    }

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var message = Encoding.UTF8.GetBytes(timestamp + "." + payload);
        var hash = hmac.ComputeHash(message);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
