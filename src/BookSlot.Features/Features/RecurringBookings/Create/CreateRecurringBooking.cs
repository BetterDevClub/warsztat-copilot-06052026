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

namespace BookSlot.Features.RecurringBookings.Create;

/// <summary>
/// Admin-only: creates a recurring booking template. The worker job (Phase 23)
/// materialises individual <c>Booking</c> rows on a rolling horizon.
/// </summary>
public static class CreateRecurringBooking
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        Guid StaffId,
        Guid ServiceTypeId,
        int IntervalWeeks,
        DayOfWeek DayOfWeek,
        TimeOnly LocalStartTime,
        DateOnly StartDate,
        DateOnly? EndDate,
        string GuestName,
        string GuestEmail,
        string? GuestPhone,
        string? GuestNotes);

    /// <summary>Response.</summary>
    public sealed record Response(
        Guid Id,
        int IntervalWeeks,
        DayOfWeek DayOfWeek,
        TimeOnly LocalStartTime,
        DateOnly StartDate,
        DateOnly? EndDate,
        string Status);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.StaffId).NotEmpty();
            RuleFor(c => c.ServiceTypeId).NotEmpty();
            RuleFor(c => c.IntervalWeeks)
                .InclusiveBetween(RecurringBooking.MinIntervalWeeks, RecurringBooking.MaxIntervalWeeks);
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

        /// <summary>Creates the recurring template.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            var staffExists = await _db.Staff.AsNoTracking()
                .AnyAsync(s => s.Id == command.StaffId && s.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (!staffExists)
                return Result.Failure<Response>(RecurringBookingErrors.StaffNotFound);

            var serviceExists = await _db.ServiceTypes.AsNoTracking()
                .AnyAsync(t => t.Id == command.ServiceTypeId && t.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (!serviceExists)
                return Result.Failure<Response>(RecurringBookingErrors.ServiceTypeNotFound);

            var result = RecurringBooking.Create(
                Guid.NewGuid(),
                _tenant.TenantId!.Value,
                command.StaffId,
                command.ServiceTypeId,
                command.IntervalWeeks,
                command.DayOfWeek,
                command.LocalStartTime,
                command.StartDate,
                command.EndDate,
                command.GuestName,
                command.GuestEmail,
                command.GuestPhone,
                command.GuestNotes,
                now);

            if (result.IsFailure)
                return Result.Failure<Response>(result.Error);

            _db.RecurringBookings.Add(result.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var r = result.Value;
            return Result.Success(new Response(
                r.Id, r.IntervalWeeks, r.DayOfWeek, r.LocalStartTime, r.StartDate, r.EndDate, r.Status.ToString()));
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
            app.MapPost("/recurring-bookings", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("RecurringBookings.Create")
                .WithTags("Recurring Bookings")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
