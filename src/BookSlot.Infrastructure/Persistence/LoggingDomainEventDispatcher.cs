using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Persistence;

/// <summary>
/// Default in-process dispatcher: logs each event and completes. Real handler wiring
/// arrives in later phases (notifications, webhooks) once we have handlers to resolve
/// from <see cref="IServiceProvider"/>; Phase 16 replaces this with the outbox variant.
/// </summary>
public sealed class LoggingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly ILogger<LoggingDomainEventDispatcher> _logger;

    /// <summary>Creates a new dispatcher.</summary>
    public LoggingDomainEventDispatcher(ILogger<LoggingDomainEventDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            _logger.LogInformation(
                "Domain event dispatched: {EventType} (EventId={EventId}, OccurredAt={OccurredAt:O})",
                domainEvent.GetType().Name,
                domainEvent.EventId,
                domainEvent.OccurredAt);
        }

        return Task.CompletedTask;
    }
}
