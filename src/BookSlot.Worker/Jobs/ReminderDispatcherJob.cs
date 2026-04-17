using BookSlot.Domain.Bookings;
using BookSlot.Domain.Notifications;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Worker.Composition;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Sends T-24h and T-2h email reminders for upcoming confirmed bookings.
/// Idempotency is delegated to <see cref="INotificationDispatcher"/> — each
/// dispatch uses a stable dedup key (<c>booking:{id}:reminder_t24h</c> /
/// <c>booking:{id}:reminder_t2h</c>) so re-runs of the job, crashes and
/// replicas all converge to a single delivery per booking per reminder tier.
/// </summary>
internal sealed class ReminderDispatcherJob : IWorkerJob
{
    private static readonly TimeSpan LookAhead24h = TimeSpan.FromHours(24);
    private static readonly TimeSpan LookAhead2h = TimeSpan.FromHours(2);

    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReminderDispatcherJob> _logger;

    public ReminderDispatcherJob(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        TimeProvider clock,
        ILogger<ReminderDispatcherJob> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "reminder-dispatcher";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var horizon = now + LookAhead24h + TimeSpan.FromMinutes(1);

        // Pull every confirmed booking whose start lies inside the widest reminder window.
        // Filtering happens client-side on a small slice (<=24h of bookings) — keeps the
        // query portable and avoids per-tier SELECTs.
        var candidates = await _db.Bookings
            .IgnoreQueryFilters()
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.StartUtc > now
                        && b.StartUtc <= horizon)
            .Select(b => new BookingSnapshot(
                b.Id, b.TenantId, b.GuestEmail, b.GuestName, b.StartUtc, b.StaffId, b.ServiceTypeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var b in candidates)
        {
            var untilStart = b.StartUtc - now;

            if (untilStart <= LookAhead24h && untilStart > LookAhead2h)
            {
                using var _ = AmbientCurrentTenant.EnterScope(b.TenantId, slug: "worker");
                await DispatchAsync(b, NotificationKind.ReminderT24h, "reminder_t24h", now, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (untilStart <= LookAhead2h)
            {
                using var _ = AmbientCurrentTenant.EnterScope(b.TenantId, slug: "worker");
                await DispatchAsync(b, NotificationKind.ReminderT2h, "reminder_t2h", now, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchAsync(
        BookingSnapshot b,
        NotificationKind kind,
        string dedupSuffix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, object?>
        {
            ["bookingId"] = b.Id,
            ["guestName"] = b.GuestName,
            ["startUtc"] = b.StartUtc,
            ["staffId"] = b.StaffId,
            ["serviceTypeId"] = b.ServiceTypeId,
            ["now"] = now,
        };

        var request = new NotificationRequest(
            TenantId: b.TenantId,
            Kind: kind,
            Channel: NotificationChannel.Email,
            Recipient: b.GuestEmail,
            DedupKey: $"booking:{b.Id}:{dedupSuffix}",
            TemplateContext: context);

        try
        {
            var result = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Duplicate)
            {
                _logger.LogInformation("Dispatched {Kind} for booking {Booking} → {Status}.",
                    kind, b.Id, result.Status);
            }
        }
#pragma warning disable CA1031 // worker jobs absorb per-booking failures to keep the batch moving
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Reminder dispatch threw for booking {Booking} ({Kind}).", b.Id, kind);
        }
    }

    private sealed record BookingSnapshot(
        Guid Id,
        Guid TenantId,
        string GuestEmail,
        string GuestName,
        DateTimeOffset StartUtc,
        Guid StaffId,
        Guid ServiceTypeId);
}
