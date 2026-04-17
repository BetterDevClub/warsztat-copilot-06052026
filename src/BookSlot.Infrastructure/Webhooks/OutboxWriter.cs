using BookSlot.Domain.Webhooks;
using BookSlot.Infrastructure.Persistence;

namespace BookSlot.Infrastructure.Webhooks;

/// <summary>
/// Default <see cref="IOutboxWriter"/> — enlists the outbox row with the ambient
/// <see cref="AppDbContext"/> so it commits in the same transaction as the domain
/// change that produced the event.
/// </summary>
internal sealed class OutboxWriter : IOutboxWriter
{
    private readonly AppDbContext _db;

    /// <summary>Creates a new writer.</summary>
    public OutboxWriter(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public void Enqueue(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _db.OutboxMessages.Add(message);
    }
}
