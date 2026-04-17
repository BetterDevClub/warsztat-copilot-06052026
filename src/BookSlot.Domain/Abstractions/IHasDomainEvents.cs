using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Structural contract exposed by <see cref="AggregateRoot{TId}"/>. Lets infrastructure
/// code reason about domain-event carriers without knowing the concrete <c>TId</c>.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Events raised but not yet dispatched.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Clears pending events.</summary>
    void ClearDomainEvents();
}
