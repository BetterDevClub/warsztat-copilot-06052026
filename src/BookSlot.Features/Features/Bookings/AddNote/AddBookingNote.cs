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

namespace BookSlot.Features.Bookings.AddNote;

/// <summary>Staff or owner appends an internal note to a booking.</summary>
public static class AddBookingNote
{
    /// <summary>Request body.</summary>
    public sealed record Command(string Content);

    /// <summary>Successful response payload.</summary>
    public sealed record Response(Guid NoteId, DateTimeOffset CreatedAt);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.Content)
                .NotEmpty()
                .MaximumLength(BookingNote.MaxContentLength);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;
        private readonly ICurrentUser _currentUser;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock, ICurrentUser currentUser)
        {
            _db = db;
            _clock = clock;
            _currentUser = currentUser;
        }

        /// <summary>Appends the note.</summary>
        public async Task<Result<Response>> HandleAsync(Guid bookingId, Command command, CancellationToken cancellationToken)
        {
            if (_currentUser.UserId is not { } authorId)
                return Result.Failure<Response>(Error.Unauthorized("BookingNote.AuthorUnknown",
                    "Authenticated user identity could not be resolved."));

            var booking = await _db.Bookings
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken)
                .ConfigureAwait(false);

            if (booking is null)
                return Result.Failure<Response>(BookingFeatureErrors.BookingNotFound);

            var count = await _db.BookingNotes
                .CountAsync(n => n.BookingId == bookingId, cancellationToken)
                .ConfigureAwait(false);

            var now = _clock.GetUtcNow();
            var noteResult = BookingNote.Create(
                Guid.NewGuid(),
                booking.TenantId,
                bookingId,
                authorId,
                command.Content,
                now,
                count);

            if (noteResult.IsFailure)
            {
                var error = noteResult.Error;
                return error.Code == "BookingNote.TooMany"
                    ? Result.Failure<Response>(BookingFeatureErrors.NotesLimitReached)
                    : Result.Failure<Response>(error);
            }

            _db.BookingNotes.Add(noteResult.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(noteResult.Value.Id, noteResult.Value.CreatedAt));
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
            app.MapPost("/admin/bookings/{id:guid}/notes", async (
                    Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("Bookings.AddNote")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
