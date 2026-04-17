namespace BookSlot.Domain.Primitives;

/// <summary>
/// Root of a DDD aggregate. Collects <see cref="IDomainEvent"/> instances
/// raised during a unit of work; infrastructure dispatches and clears them after commit.
/// </summary>
/// <typeparam name="TId">The identity type.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Parameterless constructor required by EF Core.</summary>
    protected AggregateRoot() { }

    /// <summary>Creates an aggregate root with the given identity.</summary>
    protected AggregateRoot(TId id) : base(id) { }

    /// <summary>Events raised but not yet dispatched. Read-only for callers.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Raises a domain event from inside aggregate behaviour.</summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Clears pending events; called by infrastructure after successful dispatch.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
