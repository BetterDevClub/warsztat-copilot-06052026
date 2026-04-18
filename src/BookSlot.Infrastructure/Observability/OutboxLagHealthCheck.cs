using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookSlot.Infrastructure.Observability;

/// <summary>
/// Reports degraded/unhealthy when the webhook outbox accumulates too many
/// unprocessed messages or contains a message older than the configured age.
/// Healthy = backlog &lt; warn threshold AND oldest pending &lt; warn age.
/// Degraded = backlog ≥ warn but &lt; critical thresholds.
/// Unhealthy = backlog ≥ critical or oldest pending ≥ critical age.
/// </summary>
public sealed class OutboxLagHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    private const int WarnBacklog = 50;
    private const int CriticalBacklog = 500;
    private static readonly TimeSpan WarnAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CriticalAge = TimeSpan.FromMinutes(30);

    public OutboxLagHealthCheck(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var pending = _db.OutboxMessages.IgnoreQueryFilters()
            .Where(m => m.ProcessedAt == null);

        var backlog = await pending.CountAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? oldest = null;
        if (backlog > 0)
        {
            oldest = await pending.MinAsync(m => (DateTimeOffset?)m.OccurredAt, cancellationToken)
                .ConfigureAwait(false);
        }

        var oldestAge = oldest is null ? TimeSpan.Zero : now - oldest.Value;
        var data = new Dictionary<string, object>
        {
            ["backlog"] = backlog,
            ["oldestPendingSeconds"] = (int)oldestAge.TotalSeconds,
        };

        if (backlog >= CriticalBacklog || oldestAge >= CriticalAge)
        {
            return HealthCheckResult.Unhealthy(
                $"Outbox backlog={backlog}, oldest={oldestAge.TotalSeconds:F0}s", data: data);
        }
        if (backlog >= WarnBacklog || oldestAge >= WarnAge)
        {
            return HealthCheckResult.Degraded(
                $"Outbox backlog={backlog}, oldest={oldestAge.TotalSeconds:F0}s", data: data);
        }
        return HealthCheckResult.Healthy("Outbox draining", data);
    }
}
