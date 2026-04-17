using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.Reservations;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Reservations.Create;

/// <summary>
/// Reserves a slot for a guest for <see cref="SlotReservation.TtlMinutes"/> minutes.
/// A Redis lock ensures atomicity: two concurrent requests for the same slot will result
/// in one success and one 409 Conflict.
/// </summary>
public static class CreateReservation
{
    /// <summary>Lock expiry — short, just long enough to cover a single DB round-trip.</summary>
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(10);

    /// <summary>Request body.</summary>
    public sealed record Command(
        Guid StaffId,
        Guid ServiceTypeId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc);

    /// <summary>Response: the guest token and expiry time.</summary>
    public sealed record Response(
        Guid ReservationId,
        Guid GuestToken,
        DateTimeOffset ExpiresAtUtc);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.StaffId).NotEmpty();
            RuleFor(c => c.ServiceTypeId).NotEmpty();
            RuleFor(c => c.StartUtc).NotEqual(default(DateTimeOffset));
            RuleFor(c => c.EndUtc).GreaterThan(c => c.StartUtc)
                .WithMessage("EndUtc must be after StartUtc.");
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ISlotLock _slotLock;
        private readonly ICurrentTenant _tenant;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ISlotLock slotLock, ICurrentTenant tenant, TimeProvider clock)
        {
            _db = db;
            _slotLock = slotLock;
            _tenant = tenant;
            _clock = clock;
        }

        /// <summary>Attempts to create a slot reservation atomically.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var now = _clock.GetUtcNow();

            // Redis lock key is scoped to tenant + staff + start time to allow
            // concurrent reservations for different slots on the same staff member.
            var lockKey = $"bookslot:slot:{_tenant.TenantId}:{command.StaffId}:{command.StartUtc.ToUnixTimeMilliseconds()}";

            await using var lockHandle = await _slotLock.TryAcquireAsync(lockKey, LockExpiry, cancellationToken).ConfigureAwait(false);

            if (lockHandle is null)
                return Result.Failure<Response>(ReservationErrors.LockContention);

            // Check for any active (non-expired) reservation that overlaps the requested window.
            var hasConflict = await _db.SlotReservations.AnyAsync(
                r => r.StaffId == command.StaffId
                  && r.ExpiresAtUtc > now
                  && r.StartUtc < command.EndUtc
                  && r.EndUtc > command.StartUtc,
                cancellationToken).ConfigureAwait(false);

            if (hasConflict)
                return Result.Failure<Response>(ReservationErrors.SlotAlreadyReserved);

            var reservation = SlotReservation.Create(
                Guid.NewGuid(),
                _tenant.TenantId!.Value,
                command.StaffId,
                command.ServiceTypeId,
                command.StartUtc,
                command.EndUtc,
                now);

            _db.SlotReservations.Add(reservation);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Lock released automatically when lockHandle is disposed (await using above).
            return Result.Success(new Response(reservation.Id, reservation.GuestToken, reservation.ExpiresAtUtc));
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
            app.MapPost("/reservations", async (Command command, Handler handler, CancellationToken ct) =>
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
                .WithName("Reservations.Create")
                .WithTags("Reservations")
                .AllowAnonymous()
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status409Conflict);
        }
    }
}
