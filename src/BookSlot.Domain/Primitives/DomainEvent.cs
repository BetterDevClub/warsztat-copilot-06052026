namespace BookSlot.Domain.Primitives;

/// <summary>Convenience base class for <see cref="IDomainEvent"/> implementations.</summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
