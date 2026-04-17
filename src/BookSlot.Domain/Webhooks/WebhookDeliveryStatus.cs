namespace BookSlot.Domain.Webhooks;

/// <summary>Lifecycle state of a single <see cref="WebhookDelivery"/> attempt chain.</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Queued, no attempt made yet.</summary>
    Pending = 0,

    /// <summary>An HTTP attempt is currently in flight (claimed by a worker).</summary>
    InFlight = 1,

    /// <summary>Subscriber acknowledged (2xx).</summary>
    Succeeded = 2,

    /// <summary>Last attempt failed; another retry will be attempted.</summary>
    Failed = 3,

    /// <summary>Maximum retries reached; no further attempts will be made.</summary>
    Exhausted = 4,
}
