using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Webhooks;

/// <summary>
/// Transactional outbox row. Domain events are persisted here in the same
/// transaction that mutates the aggregate so the outbox worker (Phase 24) can
/// later fan them out to webhook subscribers at-least-once.
/// </summary>
public sealed class OutboxMessage : Entity<Guid>
{
    /// <summary>Maximum length of the event type string.</summary>
    public const int MaxEventTypeLength = 128;

    private OutboxMessage() { }

    private OutboxMessage(
        Guid id,
        Guid? tenantId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt) : base(id)
    {
        TenantId = tenantId;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    /// <summary>Owning tenant, or null for system-level events.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Event type (see <see cref="WebhookEventTypes"/>).</summary>
    public string EventType { get; private set; } = default!;

    /// <summary>JSON payload.</summary>
    public string Payload { get; private set; } = default!;

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>UTC timestamp when the worker fanned the event out, or null if still pending.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Number of fan-out attempts.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Last error seen by the fan-out worker, truncated.</summary>
    public string? LastError { get; private set; }

    // -------------------------------------------------------------------------

    /// <summary>Marks the message successfully fanned out.</summary>
    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
        AttemptCount++;
    }

    /// <summary>Records a fan-out failure.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset now)
    {
        AttemptCount++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        _ = now; // attempt timestamp is carried by AttemptCount + external audit
    }

    // -------------------------------------------------------------------------

    /// <summary>Creates a new unprocessed outbox message.</summary>
    public static OutboxMessage Create(
        Guid? tenantId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        if (eventType.Length > MaxEventTypeLength)
            throw new ArgumentException($"EventType must be {MaxEventTypeLength} characters or fewer.", nameof(eventType));
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return new OutboxMessage(Guid.NewGuid(), tenantId, eventType, payload, occurredAt);
    }
}
