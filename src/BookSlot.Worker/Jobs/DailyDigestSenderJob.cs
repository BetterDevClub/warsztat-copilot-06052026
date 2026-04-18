using BookSlot.Domain.Bookings;
using BookSlot.Domain.Notifications;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Worker.Composition;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Emails each tenant's configured contact a digest of tomorrow's confirmed
/// bookings. The job ticks every 15 minutes; it converts UTC "now" into each
/// tenant's local timezone and only acts inside the
/// <see cref="DispatchWindowStart"/>–<see cref="DispatchWindowEnd"/> window
/// (18:00–18:30 local). Idempotency is enforced by the notification dispatcher
/// using the dedup key <c>digest:{tenant}:{localDate}</c> so a digest is sent
/// at most once per tenant per day regardless of how many ticks fall inside
/// the window.
/// </summary>
internal sealed class DailyDigestSenderJob : IWorkerJob
{
    private static readonly TimeOnly DispatchWindowStart = new(18, 0);
    private static readonly TimeOnly DispatchWindowEnd = new(18, 30);

    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<DailyDigestSenderJob> _logger;

    public DailyDigestSenderJob(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        TimeProvider clock,
        ILogger<DailyDigestSenderJob> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "daily-digest-sender";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var tenants = await _db.TenantSettings
            .IgnoreQueryFilters()
            .Where(s => s.ContactEmail != null)
            .Select(s => new TenantDigestTarget(s.TenantId, s.TimeZoneId, s.ContactEmail!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var t in tenants)
        {
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(t.TimeZoneId); }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Tenant {Tenant} has unknown timezone {Tz} — skipping digest.",
                    t.TenantId, t.TimeZoneId);
                continue;
            }

            var localNow = TimeZoneInfo.ConvertTime(now, tz);
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);
            if (localTime < DispatchWindowStart || localTime >= DispatchWindowEnd) continue;

            var localTomorrow = DateOnly.FromDateTime(localNow.DateTime).AddDays(1);
            var startLocal = localTomorrow.ToDateTime(TimeOnly.MinValue);
            var endLocal = localTomorrow.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

            var bookings = await _db.Bookings
                .IgnoreQueryFilters()
                .Where(b => b.TenantId == t.TenantId
                            && b.Status == BookingStatus.Confirmed
                            && b.StartUtc >= startUtc && b.StartUtc < endUtc)
                .OrderBy(b => b.StartUtc)
                .Select(b => new DigestEntry(b.Id, b.GuestName, b.StartUtc, b.StaffId, b.ServiceTypeId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            using var _ = AmbientCurrentTenant.EnterScope(t.TenantId, slug: "worker");
            var context = new Dictionary<string, object?>
            {
                ["tenantId"] = t.TenantId,
                ["localDate"] = localTomorrow,
                ["bookingCount"] = bookings.Count,
                ["bookings"] = bookings,
            };
            var dedupKey = $"digest:{t.TenantId:N}:{localTomorrow:yyyyMMdd}";

            try
            {
                var result = await _dispatcher.DispatchAsync(new NotificationRequest(
                    t.TenantId, NotificationKind.DailyDigest, NotificationChannel.Email,
                    t.ContactEmail, dedupKey, context), cancellationToken).ConfigureAwait(false);
                if (!result.Duplicate)
                {
                    _logger.LogInformation("Daily digest dispatched for tenant {Tenant} ({Count} bookings).",
                        t.TenantId, bookings.Count);
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Daily digest failed for tenant {Tenant}.", t.TenantId);
            }
        }
    }

    private sealed record TenantDigestTarget(Guid TenantId, string TimeZoneId, string ContactEmail);

    private sealed record DigestEntry(Guid Id, string GuestName, DateTimeOffset StartUtc, Guid StaffId, Guid ServiceTypeId);
}
