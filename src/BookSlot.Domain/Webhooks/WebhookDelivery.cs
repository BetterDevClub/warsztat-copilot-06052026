using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Webhooks;

/// <summary>
/// A single outbound delivery attempt-chain to a <see cref="WebhookEndpoint"/>.
/// Created by the delivery worker after it reads an outbox message, one row per
/// <c>(OutboxMessage, WebhookEndpoint)</c> pair. Kept for audit and manual retry.
/// </summary>
public sealed class WebhookDelivery : Entity<Guid>, ITenantScoped
{
    /// <summary>Maximum length of captured response body / error message.</summary>
    public const int MaxResponseSnippetLength = 2000;

    private WebhookDelivery() { }

    private WebhookDelivery(
        Guid id,
        Guid tenantId,
        Guid endpointId,
        string eventType,
        string payload,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        EndpointId = endpointId;
        EventType = eventType;
        Payload = payload;
        Status = WebhookDeliveryStatus.Pending;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The target endpoint.</summary>
    public Guid EndpointId { get; private set; }

    /// <summary>The event type (see <see cref="WebhookEventTypes"/>).</summary>
    public string EventType { get; private set; } = default!;

    /// <summary>The serialised JSON payload sent to the subscriber.</summary>
    public string Payload { get; private set; } = default!;

    /// <summary>Current lifecycle state.</summary>
    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>HTTP status code of the last attempt, if any.</summary>
    public int? LastStatusCode { get; private set; }

    /// <summary>Truncated response body or transport error from the last attempt.</summary>
    public string? LastResponseSnippet { get; private set; }

    /// <summary>Number of attempts made so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>UTC timestamp at which the next attempt is eligible.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>UTC timestamp of the last attempt.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    // -------------------------------------------------------------------------

    /// <summary>Marks the delivery in-flight (claimed by a worker).</summary>
    public void MarkInFlight(DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.InFlight;
        LastAttemptAt = now;
        AttemptCount++;
    }

    /// <summary>Records a successful attempt.</summary>
    public void MarkSucceeded(int statusCode, string? responseSnippet, DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Succeeded;
        LastStatusCode = statusCode;
        LastResponseSnippet = Truncate(responseSnippet);
        NextAttemptAt = null;
        LastAttemptAt = now;
    }

    /// <summary>Records a failed attempt and schedules the next retry.</summary>
    public void MarkFailed(int? statusCode, string? responseSnippet, DateTimeOffset nextAttemptAt, DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Failed;
        LastStatusCode = statusCode;
        LastResponseSnippet = Truncate(responseSnippet);
        NextAttemptAt = nextAttemptAt;
        LastAttemptAt = now;
    }

    /// <summary>Records that the retry budget is exhausted.</summary>
    public void MarkExhausted(int? statusCode, string? responseSnippet, DateTimeOffset now)
    {
        Status = WebhookDeliveryStatus.Exhausted;
        LastStatusCode = statusCode;
        LastResponseSnippet = Truncate(responseSnippet);
        NextAttemptAt = null;
        LastAttemptAt = now;
    }

    /// <summary>Resets the delivery to Pending for an immediate admin-triggered retry.</summary>
    public Result RequestRetry(DateTimeOffset now)
    {
        if (Status == WebhookDeliveryStatus.InFlight)
            return Result.Failure(Primitives.Error.Validation("WebhookDelivery.InFlight",
                "Delivery is currently in flight and cannot be retried."));

        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = now;
        return Result.Success();
    }

    // -------------------------------------------------------------------------

    /// <summary>Creates a new pending delivery.</summary>
    public static WebhookDelivery Enqueue(
        Guid id,
        Guid tenantId,
        Guid endpointId,
        string eventType,
        string payload,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return new WebhookDelivery(id, tenantId, endpointId, eventType, payload, now);
    }

    private static string? Truncate(string? value)
    {
        if (value is null) return null;
        return value.Length <= MaxResponseSnippetLength ? value : value[..MaxResponseSnippetLength];
    }
}
