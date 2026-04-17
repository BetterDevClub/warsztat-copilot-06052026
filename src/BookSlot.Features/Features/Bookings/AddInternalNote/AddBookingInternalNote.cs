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

namespace BookSlot.Features.Bookings.AddInternalNote;

/// <summary>Admin sets or replaces the internal note on a booking.</summary>
public static class AddBookingInternalNote
{
    /// <summary>Request body.</summary>
    public sealed record Command(string? InternalNotes);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.InternalNotes)
                .MaximumLength(Booking.MaxInternalNotesLength)
                .When(c => c.InternalNotes is not null);
        }
    }

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

        /// <summary>Updates the internal note.</summary>
        public async Task<Result> HandleAsync(Guid id, Command command, CancellationToken cancellationToken)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (booking is null)
                return Result.Failure(BookingFeatureErrors.BookingNotFound);

            booking.SetInternalNotes(command.InternalNotes, _clock.GetUtcNow());

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
            app.MapPut("/admin/bookings/{id:guid}/internal-note", async (
                    Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("Bookings.AddInternalNote")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireStaff")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
