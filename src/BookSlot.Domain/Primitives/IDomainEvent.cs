namespace BookSlot.Domain.Primitives;

/// <summary>
/// Marker for domain events raised by aggregate roots. Dispatched after
/// the transaction that produced them commits successfully.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique id of this event instance (for deduplication / logging).</summary>
    Guid EventId { get; }

    /// <summary>UTC moment the event was raised.</summary>
    DateTimeOffset OccurredAt { get; }
}
