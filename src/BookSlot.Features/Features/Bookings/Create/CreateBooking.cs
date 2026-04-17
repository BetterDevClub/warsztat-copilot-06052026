using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Bookings;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.Create;

/// <summary>
/// Creates a confirmed booking by consuming a previously created <c>SlotReservation</c>.
/// The reservation must be active; if the booking table write fails with a concurrency
/// exception the endpoint returns 409 — the slot was taken by a concurrent request.
/// </summary>
public static class CreateBooking
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        Guid ReservationId,
        string GuestName,
        string GuestEmail,
        string? GuestPhone,
        string? GuestNotes);

    /// <summary>Response.</summary>
    public sealed record Response(
        Guid BookingId,
        Guid StaffId,
        Guid ServiceTypeId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Status,
        Guid CancelToken,
        Guid RescheduleToken,
        DateTimeOffset CreatedAt);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.ReservationId).NotEmpty();
            RuleFor(c => c.GuestName).NotEmpty().MaximumLength(Booking.MaxGuestNameLength);
            RuleFor(c => c.GuestEmail).NotEmpty().MaximumLength(Booking.MaxGuestEmailLength).EmailAddress();
            RuleFor(c => c.GuestPhone).MaximumLength(Booking.MaxGuestPhoneLength).When(c => c.GuestPhone is not null);
            RuleFor(c => c.GuestNotes).MaximumLength(Booking.MaxGuestNotesLength).When(c => c.GuestNotes is not null);
        }
    }

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

        /// <summary>Creates the booking in a single round-trip.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            // Load reservation (global tenant filter applied automatically).
            var reservation = await _db.SlotReservations
                .FirstOrDefaultAsync(r => r.Id == command.ReservationId, cancellationToken)
                .ConfigureAwait(false);

            if (reservation is null)
                return Result.Failure<Response>(BookingFeatureErrors.ReservationNotFound);

            if (!reservation.IsActive(now))
                return Result.Failure<Response>(BookingFeatureErrors.ReservationExpired);

            var bookingResult = Booking.Create(
                Guid.NewGuid(),
                _tenant.TenantId!.Value,
                reservation.StaffId,
                reservation.ServiceTypeId,
                reservation.StartUtc,
                reservation.EndUtc,
                command.GuestName,
                command.GuestEmail,
                command.GuestPhone,
                command.GuestNotes,
                rescheduledFromId: null,
                now);

            if (bookingResult.IsFailure)
                return Result.Failure<Response>(bookingResult.Error);

            var booking = bookingResult.Value;
            _db.Bookings.Add(booking);
            _db.SlotReservations.Remove(reservation);

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<Response>(BookingFeatureErrors.ConcurrencyConflict);
            }

            return Result.Success(Map(booking));
        }

        private static Response Map(Booking b) => new(
            b.Id, b.StaffId, b.ServiceTypeId, b.StartUtc, b.EndUtc,
            b.Status.ToString(), b.CancelToken, b.RescheduleToken, b.CreatedAt);
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
            app.MapPost("/bookings", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(
                            validation.Errors.GroupBy(e => e.PropertyName)
                                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("Bookings.Create")
                .WithTags("Bookings")
                .AllowAnonymous()
                .RequireRateLimiting("bookings-public")
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status409Conflict);
        }
    }
}
