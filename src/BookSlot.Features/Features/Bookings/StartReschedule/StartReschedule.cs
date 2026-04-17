using BookSlot.Domain.Availability;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.StartReschedule;

/// <summary>
/// Returns available slots for rescheduling. The guest supplies their reschedule token
/// and a time window; the engine excludes the current booking from busy intervals so the
/// guest can re-select the same time if they want to.
/// </summary>
public static class StartReschedule
{
    private const int MaxWindowDays = 60;

    /// <summary>Available slot.</summary>
    public sealed record SlotDto(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    /// <summary>Response.</summary>
    public sealed record Response(
        Guid BookingId,
        Guid ServiceTypeId,
        IReadOnlyList<SlotDto> AvailableSlots);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        /// <summary>Loads booking and computes available slots.</summary>
        public async Task<Result<Response>> HandleAsync(
            Guid rescheduleToken,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            if (to <= from || (to - from).TotalDays > MaxWindowDays)
                return Result.Failure<Response>(Error.Validation("Reschedule.InvalidWindow",
                    $"Window must be positive and at most {MaxWindowDays} days."));

            var booking = await _db.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.RescheduleToken == rescheduleToken, cancellationToken)
                .ConfigureAwait(false);

            if (booking is null)
                return Result.Failure<Response>(BookingFeatureErrors.InvalidRescheduleToken);

            var service = await _db.ServiceTypes.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == booking.ServiceTypeId && s.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (service is null)
                return Result.Failure<Response>(BookingFeatureErrors.ServiceTypeNotFound);

            var settings = await _db.TenantSettings.AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(settings?.TimeZoneId ?? "UTC"); }
            catch { tz = TimeZoneInfo.Utc; }

            var rules = await _db.AvailabilityRules.AsNoTracking()
                .Where(r => r.StaffId == booking.StaffId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var overrides = await _db.AvailabilityOverrides.AsNoTracking()
                .Where(o => o.StaffId == booking.StaffId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            // Treat all active reservations + confirmed bookings as busy intervals,
            // but exclude the current booking so the guest can re-choose the same slot.
            var now = _clock.GetUtcNow();
            var busyBookings = await _db.Bookings.AsNoTracking()
                .Where(b => b.StaffId == booking.StaffId
                         && b.Id != booking.Id
                         && b.StartUtc < to
                         && b.EndUtc > from
                         && (b.Status == Domain.Bookings.BookingStatus.Confirmed
                          || b.Status == Domain.Bookings.BookingStatus.Pending))
                .Select(b => new { b.StartUtc, b.EndUtc })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var busyReservations = await _db.SlotReservations.AsNoTracking()
                .Where(r => r.StaffId == booking.StaffId
                         && r.StartUtc < to
                         && r.EndUtc > from
                         && r.ExpiresAtUtc > now)
                .Select(r => new { r.StartUtc, r.EndUtc })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var busy = busyBookings.Select(b => new BusyInterval(b.StartUtc, b.EndUtc))
                .Concat(busyReservations.Select(r => new BusyInterval(r.StartUtc, r.EndUtc)))
                .ToList();

            var request = new AvailabilityRequest
            {
                TimeZone = tz,
                FromUtc = from,
                ToUtc = to,
                DurationMinutes = service.DurationMinutes,
                BufferBeforeMinutes = service.BufferBeforeMinutes,
                BufferAfterMinutes = service.BufferAfterMinutes,
                Rules = rules,
                Overrides = overrides,
                Busy = busy,
            };

            var engineResult = AvailabilityEngine.GenerateSlots(request);
            if (engineResult.IsFailure)
                return Result.Failure<Response>(engineResult.Error);

            var slots = engineResult.Value
                .Select(s => new SlotDto(s.StartUtc, s.EndUtc))
                .ToList();

            return Result.Success(new Response(booking.Id, booking.ServiceTypeId, slots));
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapGet("/bookings/reschedule/{rescheduleToken:guid}", async (
                    Guid rescheduleToken,
                    DateTimeOffset from,
                    DateTimeOffset to,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(rescheduleToken, from, to, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Bookings.StartReschedule")
                .WithTags("Bookings")
                .AllowAnonymous()
                .Produces<Response>()
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
