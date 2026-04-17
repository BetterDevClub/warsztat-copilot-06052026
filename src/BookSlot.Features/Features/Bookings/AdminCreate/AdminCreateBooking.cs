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

namespace BookSlot.Features.Bookings.AdminCreate;

/// <summary>
/// Admin-only manual booking creation. Unlike the public flow this endpoint
/// does NOT require a <c>SlotReservation</c> and can override availability
/// (admins explicitly book outside working hours when <c>Force=true</c>).
/// </summary>
public static class AdminCreateBooking
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        Guid StaffId,
        Guid ServiceTypeId,
        DateTimeOffset StartUtc,
        string GuestName,
        string GuestEmail,
        string? GuestPhone,
        string? GuestNotes,
        string? InternalNotes,
        bool Force,
        Dictionary<string, System.Text.Json.JsonElement>? CustomFieldValues = null);

    /// <summary>Response.</summary>
    public sealed record Response(
        Guid BookingId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Status,
        Guid CancelToken,
        Guid RescheduleToken);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.StaffId).NotEmpty();
            RuleFor(c => c.ServiceTypeId).NotEmpty();
            RuleFor(c => c.GuestName).NotEmpty().MaximumLength(Booking.MaxGuestNameLength);
            RuleFor(c => c.GuestEmail).NotEmpty().MaximumLength(Booking.MaxGuestEmailLength).EmailAddress();
            RuleFor(c => c.GuestPhone).MaximumLength(Booking.MaxGuestPhoneLength).When(c => c.GuestPhone is not null);
            RuleFor(c => c.GuestNotes).MaximumLength(Booking.MaxGuestNotesLength).When(c => c.GuestNotes is not null);
            RuleFor(c => c.InternalNotes).MaximumLength(Booking.MaxInternalNotesLength).When(c => c.InternalNotes is not null);
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

        /// <summary>Creates the booking — optionally overriding availability checks.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            var staff = await _db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == command.StaffId && s.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (staff is null)
                return Result.Failure<Response>(BookingFeatureErrors.StaffNotFound);

            var service = await _db.ServiceTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == command.ServiceTypeId && t.IsActive, cancellationToken)
                .ConfigureAwait(false);
            if (service is null)
                return Result.Failure<Response>(BookingFeatureErrors.ServiceTypeNotFound);

            var endUtc = command.StartUtc.AddMinutes(service.DurationMinutes);

            string? customFieldsJson = null;
            if (!string.IsNullOrWhiteSpace(service.FormSchemaJson))
            {
                var parsed = Domain.Services.BookingFormSchema.Parse(service.FormSchemaJson);
                if (parsed.IsFailure) return Result.Failure<Response>(parsed.Error);

                var submitted = (IReadOnlyDictionary<string, System.Text.Json.JsonElement>?)command.CustomFieldValues
                    ?? new Dictionary<string, System.Text.Json.JsonElement>();
                var validation = parsed.Value.Validate(submitted);
                if (validation.IsFailure) return Result.Failure<Response>(validation.Error);

                if (command.CustomFieldValues is { Count: > 0 })
                    customFieldsJson = System.Text.Json.JsonSerializer.Serialize(command.CustomFieldValues);
            }
            else if (command.CustomFieldValues is { Count: > 0 })
            {
                return Result.Failure<Response>(Error.Validation(
                    "CustomFields.NoSchema", "Service type does not declare a custom form schema."));
            }

            if (!command.Force)
            {
                // Basic overlap check. When Force=true the admin accepts responsibility
                // for double-booking (walk-ins, ad-hoc overrides).
                var overlap = await _db.Bookings.AsNoTracking()
                    .AnyAsync(b => b.StaffId == command.StaffId
                                && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending)
                                && b.StartUtc < endUtc
                                && b.EndUtc > command.StartUtc, cancellationToken)
                    .ConfigureAwait(false);
                if (overlap)
                    return Result.Failure<Response>(BookingFeatureErrors.ConcurrencyConflict);
            }

            var bookingResult = Booking.Create(
                Guid.NewGuid(),
                _tenant.TenantId!.Value,
                command.StaffId,
                command.ServiceTypeId,
                command.StartUtc,
                endUtc,
                command.GuestName,
                command.GuestEmail,
                command.GuestPhone,
                command.GuestNotes,
                rescheduledFromId: null,
                now);

            if (bookingResult.IsFailure)
                return Result.Failure<Response>(bookingResult.Error);

            var booking = bookingResult.Value;
            if (customFieldsJson is not null)
                booking.SetCustomFieldValues(customFieldsJson, now);
            if (!string.IsNullOrWhiteSpace(command.InternalNotes))
                booking.SetInternalNotes(command.InternalNotes, now);

            _db.Bookings.Add(booking);

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<Response>(BookingFeatureErrors.ConcurrencyConflict);
            }

            return Result.Success(new Response(
                booking.Id, booking.StartUtc, booking.EndUtc, booking.Status.ToString(),
                booking.CancelToken, booking.RescheduleToken));
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
            app.MapPost("/admin/bookings", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("Bookings.AdminCreate")
                .WithTags("Bookings (Admin)")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);
        }
    }
}
