using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Reservations.Release;

/// <summary>
/// Explicitly releases a slot reservation before its TTL expires.
/// The guest provides their token; only the token holder can release their reservation.
/// </summary>
public static class ReleaseReservation
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        /// <summary>Deletes the reservation if the token matches.</summary>
        public async Task<Result> HandleAsync(Guid reservationId, Guid guestToken, CancellationToken cancellationToken)
        {
            var reservation = await _db.SlotReservations
                .FirstOrDefaultAsync(r => r.Id == reservationId, cancellationToken)
                .ConfigureAwait(false);

            if (reservation is null)
                return Result.Failure(ReservationErrors.NotFound);

            if (reservation.GuestToken != guestToken)
                return Result.Failure(ReservationErrors.InvalidToken);

            _db.SlotReservations.Remove(reservation);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success();
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
            app.MapDelete("/reservations/{reservationId:guid}", async (
                    Guid reservationId,
                    Guid guestToken,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(reservationId, guestToken, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("Reservations.Release")
                .WithTags("Reservations")
                .AllowAnonymous()
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status422UnprocessableEntity);
        }
    }
}
