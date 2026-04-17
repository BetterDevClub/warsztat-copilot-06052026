using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BookSlot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Collects <see cref="IDomainEvent"/> instances from aggregate roots tracked by the
/// context and dispatches them through <see cref="IDomainEventDispatcher"/> after the
/// underlying database commit succeeds. Aggregates' event buffers are cleared before
/// dispatch to prevent re-dispatch on subsequent saves.
///
/// Phase 16 replaces the in-process dispatcher with an outbox writer that persists events
/// in the same transaction; the interceptor contract stays stable — only the dispatcher
/// implementation changes.
/// </summary>
public sealed class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly List<IDomainEvent> _pending = [];

    /// <summary>Creates a new interceptor.</summary>
    public DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is not null)
        {
            CollectAndClear(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is not null)
        {
            CollectAndClear(context);
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_pending.Count > 0)
        {
            var batch = _pending.ToArray();
            _pending.Clear();
            await _dispatcher.DispatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (_pending.Count > 0)
        {
            var batch = _pending.ToArray();
            _pending.Clear();
            _dispatcher.DispatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _pending.Clear();
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CollectAndClear(DbContext context)
    {
        var aggregates = context.ChangeTracker
            .Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToArray();

        foreach (var aggregate in aggregates)
        {
            _pending.AddRange(aggregate.DomainEvents);
            aggregate.ClearDomainEvents();
        }
    }
}
