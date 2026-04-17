using BookSlot.Domain.Bookings;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Worker.Composition;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// For tenants that opted into <c>TenantSettings.NoShowAutoMarkEnabled</c>, flips
/// <see cref="BookingStatus.Confirmed"/> bookings into
/// <see cref="BookingStatus.NoShow"/> once the configured grace period elapses
/// past <c>Booking.EndUtc</c>. Per-tenant policy is respected via an
/// ambient-tenant scope so the <see cref="Domain.Bookings.Booking"/> write goes
/// through the same interceptors as a regular user-initiated update.
/// </summary>
internal sealed class NoShowMarkerJob : IWorkerJob
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<NoShowMarkerJob> _logger;

    public NoShowMarkerJob(AppDbContext db, TimeProvider clock, ILogger<NoShowMarkerJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "no-show-marker";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var tenants = await _db.TenantSettings
            .IgnoreQueryFilters()
            .Where(s => s.NoShowAutoMarkEnabled)
            .Select(s => new TenantPolicy(s.TenantId, s.NoShowGracePeriodMinutes))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tenants.Count == 0) return;

        foreach (var t in tenants)
        {
            var cutoff = now - TimeSpan.FromMinutes(t.GraceMinutes);

            // Load into tracking so MarkNoShow() mutations are persisted by SaveChanges.
            // We limit to 500/run to cap the worst-case batch size on large tenants.
            var stale = await _db.Bookings
                .IgnoreQueryFilters()
                .Where(b => b.TenantId == t.TenantId
                            && b.Status == BookingStatus.Confirmed
                            && b.EndUtc <= cutoff)
                .OrderBy(b => b.EndUtc)
                .Take(500)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stale.Count == 0) continue;

            using var _ = AmbientCurrentTenant.EnterScope(t.TenantId, slug: "worker");

            foreach (var booking in stale)
            {
                var result = booking.MarkNoShow(now);
                if (result.IsFailure)
                {
                    _logger.LogWarning("NoShowMarker refused booking {Booking}: {Error}",
                        booking.Id, result.Error.Code);
                }
            }

            var written = await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("NoShowMarker tenant={Tenant}: marked {Count} bookings (rows={Rows}).",
                t.TenantId, stale.Count, written);
        }
    }

    private sealed record TenantPolicy(Guid TenantId, int GraceMinutes);
}
