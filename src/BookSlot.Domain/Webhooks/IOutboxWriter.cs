namespace BookSlot.Domain.Webhooks;

/// <summary>
/// Appends an <see cref="OutboxMessage"/> to the outbox in the current unit of work.
/// Invoked from slices and the domain-event interceptor; the underlying implementation
/// enlists with the active <c>DbContext</c> so the row commits in the same transaction
/// as the aggregate change that produced the event.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>Queues an outbox row to be persisted on <c>SaveChangesAsync</c>.</summary>
    void Enqueue(OutboxMessage message);
}
