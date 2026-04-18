using BookSlot.Domain.Bookings;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Worker.Composition;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Materialises concrete <see cref="Booking"/> rows from active
/// <see cref="RecurringBooking"/> templates on a rolling
/// <see cref="HorizonWeeks"/>-week horizon. Each template tracks a
/// <c>LastGeneratedThrough</c> watermark so subsequent ticks only produce
/// occurrences past that cursor. Occurrences that would overlap an existing
/// booking for the same staff member are skipped (first-free strategy — no
/// auto-bump), letting an admin notice and reschedule manually.
/// </summary>
internal sealed class RecurringBookingGeneratorJob : IWorkerJob
{
    /// <summary>How many weeks ahead to materialise bookings.</summary>
    public const int HorizonWeeks = 4;

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<RecurringBookingGeneratorJob> _logger;

    public RecurringBookingGeneratorJob(
        AppDbContext db,
        TimeProvider clock,
        ILogger<RecurringBookingGeneratorJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "recurring-booking-generator";

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var templates = await _db.RecurringBookings
            .IgnoreQueryFilters()
            .Where(r => r.Status == RecurringBookingStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (templates.Count == 0) return;

        // Fetch tz map for all touched tenants in one query.
        var tenantIds = templates.Select(r => r.TenantId).Distinct().ToList();
        var tzMap = await _db.TenantSettings
            .IgnoreQueryFilters()
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId, s => s.TimeZoneId, cancellationToken)
            .ConfigureAwait(false);

        // Fetch duration+buffer for all touched service types once.
        var serviceTypeIds = templates.Select(r => r.ServiceTypeId).Distinct().ToList();
        var serviceMap = await _db.ServiceTypes
            .IgnoreQueryFilters()
            .Where(st => serviceTypeIds.Contains(st.Id))
            .Select(st => new { st.Id, st.DurationMinutes, st.BufferBeforeMinutes, st.BufferAfterMinutes })
            .ToDictionaryAsync(x => x.Id, x => (x.DurationMinutes, x.BufferBeforeMinutes, x.BufferAfterMinutes),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!tzMap.TryGetValue(template.TenantId, out var tzId)) continue;
            if (!serviceMap.TryGetValue(template.ServiceTypeId, out var svc)) continue;

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Recurring template {Template}: tenant tz {Tz} unknown.", template.Id, tzId);
                continue;
            }

            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
            var horizonEnd = localToday.AddDays(HorizonWeeks * 7);
            var start = template.LastGeneratedThrough is { } last
                ? last.AddDays(1)
                : template.StartDate;
            if (start > horizonEnd) continue;
            if (template.EndDate is { } end && horizonEnd > end) horizonEnd = end;

            var occurrences = EnumerateOccurrences(template, start, horizonEnd).ToList();
            if (occurrences.Count == 0)
            {
                template.AdvanceGenerationWatermark(horizonEnd, now);
                continue;
            }

            using var _ = AmbientCurrentTenant.EnterScope(template.TenantId, slug: "worker");

            var created = 0;
            foreach (var localDate in occurrences)
            {
                var localStart = localDate.ToDateTime(template.LocalStartTime);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
                var endUtc = startUtc.AddMinutes(svc.DurationMinutes + svc.BufferAfterMinutes);
                var bufferedStart = startUtc.AddMinutes(-svc.BufferBeforeMinutes);

                var conflict = await _db.Bookings
                    .IgnoreQueryFilters()
                    .AnyAsync(b => b.TenantId == template.TenantId
                                   && b.StaffId == template.StaffId
                                   && b.Status != BookingStatus.Cancelled
                                   && b.Status != BookingStatus.Rescheduled
                                   && b.StartUtc < endUtc
                                   && b.EndUtc > bufferedStart,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (conflict)
                {
                    _logger.LogInformation("Template {Template}: skipping {Date} — conflict on staff {Staff}.",
                        template.Id, localDate, template.StaffId);
                    continue;
                }

                var booking = Booking.Create(
                    Guid.NewGuid(), template.TenantId, template.StaffId, template.ServiceTypeId,
                    startUtc, endUtc,
                    template.GuestName, template.GuestEmail, template.GuestPhone, template.GuestNotes,
                    rescheduledFromId: null, now);
                if (booking.IsFailure)
                {
                    _logger.LogWarning("Template {Template}: Booking.Create rejected {Date}: {Err}.",
                        template.Id, localDate, booking.Error.Code);
                    continue;
                }
                _db.Bookings.Add(booking.Value);
                created++;
            }

            template.AdvanceGenerationWatermark(horizonEnd, now);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (created > 0)
            {
                _logger.LogInformation("Recurring template {Template}: generated {Count} bookings through {Through}.",
                    template.Id, created, horizonEnd);
            }
        }
    }

    private static IEnumerable<DateOnly> EnumerateOccurrences(
        RecurringBooking template, DateOnly from, DateOnly toInclusive)
    {
        var cursor = from;
        // advance cursor to the first matching day-of-week
        while (cursor <= toInclusive && cursor.DayOfWeek != template.DayOfWeek)
            cursor = cursor.AddDays(1);

        while (cursor <= toInclusive)
        {
            if (cursor >= template.StartDate) yield return cursor;
            cursor = cursor.AddDays(7 * template.IntervalWeeks);
        }
    }
}
