using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.ConfirmReschedule;

/// <summary>
/// Finalises a reschedule: creates a new <c>Booking</c> for the new slot, marks the
/// original booking as <see cref="BookingStatus.Rescheduled"/>, and releases the
/// slot reservation — all in a single DB transaction.
/// </summary>
public static class ConfirmReschedule
{
    /// <summary>Request body.</summary>
    public sealed record Command(Guid ReservationId);

    /// <summary>Response.</summary>
    public sealed record Response(
        Guid NewBookingId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Status,
        Guid CancelToken,
        Guid RescheduleToken);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
        {
            _db = db;
            _tenant = tenant;
            _clock = clock;
        }

        /// <summary>Performs the reschedule atomically.</summary>
        public async Task<Result<Response>> HandleAsync(
            Guid rescheduleToken,
            Command command,
            CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            var original = await _db.Bookings
                .FirstOrDefaultAsync(b => b.RescheduleToken == rescheduleToken, cancellationToken)
                .ConfigureAwait(false);

            if (original is null)
                return Result.Failure<Response>(BookingFeatureErrors.InvalidRescheduleToken);

            var reservation = await _db.SlotReservations
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken)
                .ConfigureAwait(false);

            if (reservation is null)
                return Result.Failure<Response>(BookingFeatureErrors.ReservationNotFound);

            if (!reservation.IsActive(now))
                return Result.Failure<Response>(BookingFeatureErrors.ReservationExpired);

            // Verify the reservation is for the same staff/service.
            if (reservation.StaffId != original.StaffId || reservation.ServiceTypeId != original.ServiceTypeId)
                return Result.Failure<Response>(Error.Validation("Reschedule.SlotMismatch",
                    "The reservation does not match the original booking's staff or service."));

            var newBookingResult = Booking.Create(
                Guid.NewGuid(),
                _tenant.TenantId!.Value,
                reservation.StaffId,
                reservation.ServiceTypeId,
                reservation.StartUtc,
                reservation.EndUtc,
                original.GuestName,
                original.GuestEmail,
                original.GuestPhone,
                original.GuestNotes,
                rescheduledFromId: original.Id,
                now);

            if (newBookingResult.IsFailure)
                return Result.Failure<Response>(newBookingResult.Error);

            var markResult = original.MarkRescheduled(now);
            if (markResult.IsFailure)
                return Result.Failure<Response>(markResult.Error);

            _db.Bookings.Add(newBookingResult.Value);
            _db.SlotReservations.Remove(reservation);

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<Response>(BookingFeatureErrors.ConcurrencyConflict);
            }

            var nb = newBookingResult.Value;
            return Result.Success(new Response(nb.Id, nb.StartUtc, nb.EndUtc, nb.Status.ToString(), nb.CancelToken, nb.RescheduleToken));
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
            app.MapPost("/bookings/reschedule/{rescheduleToken:guid}", async (
                    Guid rescheduleToken,
                    Command command,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(rescheduleToken, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("Bookings.ConfirmReschedule")
                .WithTags("Bookings")
                .AllowAnonymous()
                .Produces<Response>(StatusCodes.Status201Created)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);
        }
    }
}
