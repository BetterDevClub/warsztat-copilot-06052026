using BookSlot.Domain.Webhooks;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Fan-out stage of the transactional outbox pattern. Reads unprocessed
/// <see cref="OutboxMessage"/> rows, looks up every active, subscribed
/// <see cref="WebhookEndpoint"/> in the owning tenant, and materialises a
/// pending <see cref="WebhookDelivery"/> per (message, endpoint) pair. The
/// outbox row is then marked processed so it is never fanned out twice.
/// System-level messages (<c>TenantId is null</c>) produce no deliveries —
/// webhooks are a tenant-scoped feature — but the rows are still closed out.
/// </summary>
internal sealed class OutboxFanoutJob : IWorkerJob
{
    private const int BatchSize = 200;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxFanoutJob> _logger;

    public OutboxFanoutJob(AppDbContext db, TimeProvider clock, ILogger<OutboxFanoutJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "outbox-fanout";

    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0) return;

        // Pre-fetch endpoints for the tenants touched in this batch — one query.
        var tenantIds = pending.Where(m => m.TenantId.HasValue)
            .Select(m => m.TenantId!.Value)
            .Distinct()
            .ToList();

        var endpoints = tenantIds.Count == 0
            ? new List<WebhookEndpoint>()
            : await _db.WebhookEndpoints.IgnoreQueryFilters()
                .Where(e => e.IsActive && tenantIds.Contains(e.TenantId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var created = 0;
        foreach (var message in pending)
        {
            if (message.TenantId is Guid tenantId)
            {
                foreach (var endpoint in endpoints)
                {
                    if (endpoint.TenantId != tenantId) continue;
                    if (!endpoint.SubscribesTo(message.EventType)) continue;

                    var delivery = WebhookDelivery.Enqueue(
                        Guid.NewGuid(), tenantId, endpoint.Id,
                        message.EventType, message.Payload, now);
                    _db.WebhookDeliveries.Add(delivery);
                    created++;
                }
            }

            message.MarkProcessed(now);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Outbox fanout processed {Msgs} messages → {Deliveries} deliveries.",
            pending.Count, created);
    }
}
