using BookSlot.Domain.Bookings;
using BookSlot.Domain.Notifications;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Worker.Composition;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Emails each tenant a summary of the previous calendar month's activity —
/// booking count, no-show rate and the most-booked service. Runs hourly; the
/// tenant is only processed when their local clock is inside
/// <see cref="DispatchWindowStart"/>–<see cref="DispatchWindowEnd"/> on day 1
/// of the month. Idempotency is delegated to the notification dispatcher via
/// the dedup key <c>report:{tenant}:{yyyy}-{MM}</c>.
/// A future iteration will also persist the computed payload to blob storage
/// and include a signed download link; for now the summary is inlined in the
/// email template context so the report survives in <see cref="NotificationLog"/>.
/// </summary>
internal sealed class MonthlyReportArchiverJob : IWorkerJob
{
    private static readonly TimeOnly DispatchWindowStart = new(8, 0);
    private static readonly TimeOnly DispatchWindowEnd = new(9, 0);

    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<MonthlyReportArchiverJob> _logger;

    public MonthlyReportArchiverJob(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        TimeProvider clock,
        ILogger<MonthlyReportArchiverJob> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "monthly-report-archiver";

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var tenants = await _db.TenantSettings
            .IgnoreQueryFilters()
            .Where(s => s.ContactEmail != null)
            .Select(s => new ReportTarget(s.TenantId, s.TimeZoneId, s.ContactEmail!))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var t in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(t.TimeZoneId); }
            catch (TimeZoneNotFoundException) { continue; }

            var localNow = TimeZoneInfo.ConvertTime(now, tz);
            if (localNow.Day != 1) continue;
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);
            if (localTime < DispatchWindowStart || localTime >= DispatchWindowEnd) continue;

            // Previous calendar month in tenant-local time, converted to UTC range.
            var firstOfThisMonthLocal = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var firstOfPrevMonthLocal = firstOfThisMonthLocal.AddMonths(-1);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(firstOfPrevMonthLocal, tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(firstOfThisMonthLocal, tz);

            var stats = await ComputeStatsAsync(t.TenantId, startUtc, endUtc, cancellationToken)
                .ConfigureAwait(false);

            using var _ = AmbientCurrentTenant.EnterScope(t.TenantId, slug: "worker");
            var periodKey = $"{firstOfPrevMonthLocal:yyyy-MM}";
            var dedupKey = $"report:{t.TenantId:N}:{periodKey}";
            var context = new Dictionary<string, object?>
            {
                ["tenantId"] = t.TenantId,
                ["period"] = periodKey,
                ["totalBookings"] = stats.Total,
                ["noShowCount"] = stats.NoShow,
                ["noShowRate"] = stats.Total == 0 ? 0.0 : Math.Round((double)stats.NoShow / stats.Total, 4),
                ["cancelledCount"] = stats.Cancelled,
                ["topServiceId"] = stats.TopServiceId,
                ["topServiceCount"] = stats.TopServiceCount,
            };

            try
            {
                var result = await _dispatcher.DispatchAsync(new NotificationRequest(
                    t.TenantId, NotificationKind.MonthlyReport, NotificationChannel.Email,
                    t.ContactEmail, dedupKey, context), cancellationToken).ConfigureAwait(false);
                if (!result.Duplicate)
                {
                    _logger.LogInformation("Monthly report dispatched for tenant {Tenant} period {Period} (bookings={Total}).",
                        t.TenantId, periodKey, stats.Total);
                }
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Monthly report failed for tenant {Tenant}.", t.TenantId);
            }
        }
    }

    private async Task<MonthlyStats> ComputeStatsAsync(
        Guid tenantId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        var baseQ = _db.Bookings.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.StartUtc >= startUtc && b.StartUtc < endUtc);

        var total = await baseQ.CountAsync(ct).ConfigureAwait(false);
        var noShow = await baseQ.CountAsync(b => b.Status == BookingStatus.NoShow, ct).ConfigureAwait(false);
        var cancelled = await baseQ.CountAsync(b => b.Status == BookingStatus.Cancelled, ct).ConfigureAwait(false);

        var top = await baseQ
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.NoShow)
            .GroupBy(b => b.ServiceTypeId)
            .Select(g => new { ServiceId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return new MonthlyStats(total, noShow, cancelled, top?.ServiceId, top?.Count ?? 0);
    }

    private sealed record ReportTarget(Guid TenantId, string TimeZoneId, string ContactEmail);

    private sealed record MonthlyStats(int Total, int NoShow, int Cancelled, Guid? TopServiceId, int TopServiceCount);
}
