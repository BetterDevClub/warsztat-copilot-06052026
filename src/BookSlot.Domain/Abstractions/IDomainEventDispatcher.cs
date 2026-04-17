using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Dispatches <see cref="IDomainEvent"/> instances collected on aggregate roots to
/// registered handlers. Called by the persistence interceptor after a successful
/// <c>SaveChangesAsync</c>; the outbox-backed variant (Phase 16) persists events in the
/// same transaction instead of in-process dispatch.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches the given events; implementations must be resilient to handler failures.</summary>
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
