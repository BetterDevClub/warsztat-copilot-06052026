using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.AdminCancel;

/// <summary>
/// Admin cancels a booking by id (no token required). Same domain rule as guest cancel
/// — only Pending/Confirmed bookings can be cancelled.
/// </summary>
public static class AdminCancelBooking
{
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

        /// <summary>Cancels the booking.</summary>
        public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (booking is null)
                return Result.Failure(BookingFeatureErrors.BookingNotFound);

            var result = booking.Cancel(_clock.GetUtcNow());
            if (result.IsFailure) return result;

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(BookingFeatureErrors.ConcurrencyConflict);
            }

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
            app.MapPost("/admin/bookings/{id:guid}/cancel", async (Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("Bookings.AdminCancel")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireStaff")
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
