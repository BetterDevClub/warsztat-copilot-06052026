using BookSlot.Domain.Availability;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Availability.GetSlots;

/// <summary>
/// Returns available booking slots for a given staff member and service type within a UTC window.
/// Loads rules, overrides, service duration/buffers and existing busy intervals from the database
/// then delegates to the pure <see cref="AvailabilityEngine"/>.
/// </summary>
public static class GetSlots
{
    private const int MaxWindowDays = 60;

    /// <summary>A single available slot.</summary>
    public sealed record SlotDto(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    /// <summary>Response envelope.</summary>
    public sealed record Response(IReadOnlyList<SlotDto> Slots);

    /// <summary>Query parameters.</summary>
    public sealed record Query(
        Guid StaffId,
        Guid ServiceTypeId,
        DateTimeOffset From,
        DateTimeOffset To);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Query>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(q => q.ServiceTypeId).NotEmpty();
            RuleFor(q => q.From).NotEqual(default(DateTimeOffset));
            RuleFor(q => q.To).GreaterThan(q => q.From)
                .WithMessage("'To' must be after 'From'.");
            RuleFor(q => q)
                .Must(q => (q.To - q.From).TotalDays <= MaxWindowDays)
                .WithMessage($"Window cannot exceed {MaxWindowDays} days.");
        }
    }

    /// <summary>Slice handler — loads data from the DB and calls the availability engine.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Computes available slots.</summary>
        public async Task<Result<Response>> HandleAsync(Query query, CancellationToken cancellationToken)
        {
            // 1. Load service type (duration + buffers)
            var service = await _db.ServiceTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.ServiceTypeId && s.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (service is null)
                return Result.Failure<Response>(AvailabilityErrors.ServiceTypeNotFound);

            // 2. Load staff (must exist and be active)
            var staff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.StaffId && s.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (staff is null)
                return Result.Failure<Response>(AvailabilityErrors.StaffNotFound);

            // 3. Confirm the staff can perform this service
            var canPerform = await _db.StaffServiceAssignments
                .AsNoTracking()
                .AnyAsync(a => a.StaffId == query.StaffId && a.ServiceTypeId == query.ServiceTypeId, cancellationToken)
                .ConfigureAwait(false);

            if (!canPerform)
                return Result.Failure<Response>(AvailabilityErrors.ServiceNotAssigned);

            // 4. Load tenant settings for timezone
            var settings = await _db.TenantSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(settings?.TimeZoneId ?? "UTC");
            }
            catch (TimeZoneNotFoundException)
            {
                tz = TimeZoneInfo.Utc;
            }

            // 5. Load availability rules and overrides for the staff member
            var rules = await _db.AvailabilityRules
                .AsNoTracking()
                .Where(r => r.StaffId == query.StaffId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var overrides = await _db.AvailabilityOverrides
                .AsNoTracking()
                .Where(o => o.StaffId == query.StaffId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // 6. No busy intervals yet (bookings come in Phase 12)
            var busy = Array.Empty<BusyInterval>();

            // 7. Build request and call engine
            var request = new AvailabilityRequest
            {
                TimeZone = tz,
                FromUtc = query.From,
                ToUtc = query.To,
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

            return Result.Success(new Response(slots));
        }
    }

    /// <summary>Endpoint registration — public (no auth required) so the booking widget can call it.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapGet("/availability/{staffId:guid}", async (
                    Guid staffId,
                    Guid serviceTypeId,
                    DateTimeOffset from,
                    DateTimeOffset to,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var query = new Query(staffId, serviceTypeId, from, to);
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(query, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                    {
                        var errors = validation.Errors.Select(e => e.ErrorMessage).ToArray();
                        return Results.ValidationProblem(
                            validation.Errors.GroupBy(e => e.PropertyName)
                                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
                    }

                    var result = await handler.HandleAsync(query, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Availability.GetSlots")
                .WithTags("Availability")
                .AllowAnonymous()
                .Produces<Response>()
                .ProducesValidationProblem();
        }
    }
}
