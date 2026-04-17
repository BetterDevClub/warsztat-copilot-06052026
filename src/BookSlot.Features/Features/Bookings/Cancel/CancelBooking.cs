using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Bookings.Cancel;

/// <summary>
/// Guest self-service cancellation using the opaque <c>CancelToken</c> emailed at booking time.
/// </summary>
public static class CancelBooking
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

        /// <summary>Cancels the booking if the token matches.</summary>
        public async Task<Result> HandleAsync(Guid cancelToken, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            var booking = await _db.Bookings
                .FirstOrDefaultAsync(b => b.CancelToken == cancelToken, cancellationToken)
                .ConfigureAwait(false);

            if (booking is null)
                return Result.Failure(BookingFeatureErrors.InvalidCancelToken);

            var cancelResult = booking.Cancel(now);
            if (cancelResult.IsFailure)
                return cancelResult;

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
            app.MapPost("/bookings/cancel/{cancelToken:guid}", async (
                    Guid cancelToken,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(cancelToken, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("Bookings.Cancel")
                .WithTags("Bookings")
                .AllowAnonymous()
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
