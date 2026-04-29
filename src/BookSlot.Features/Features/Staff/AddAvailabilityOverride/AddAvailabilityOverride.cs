using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.Staff;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.AddAvailabilityOverride;

/// <summary>Adds an availability override (unavailable day, or extra-hours window). Owner only.</summary>
public static class AddAvailabilityOverride
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        DateOnly Date,
        bool IsUnavailable,
        TimeOnly? StartTime,
        TimeOnly? EndTime,
        string? Reason);

    /// <summary>Response.</summary>
    public sealed record Response(Guid Id);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Reason).MaximumLength(AvailabilityOverride.MaxReasonLength);
            When(x => !x.IsUnavailable, () =>
            {
                RuleFor(x => x.StartTime).NotNull();
                RuleFor(x => x.EndTime).NotNull();
            });
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant) { _db = db; _tenant = tenant; }

        /// <summary>Creates the override and persists it.</summary>
        public async Task<Result<Response>> HandleAsync(Guid staffId, Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (_tenant.TenantId is null)
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            var tenantId = _tenant.TenantId.Value;

            var staffExists = await _db.Staff.AnyAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
            if (!staffExists) return Result.Failure<Response>(StaffErrors.NotFound);

            // Only enforce conflict for full-day unavailable overrides; extra-hours windows can stack.
            if (command.IsUnavailable)
            {
                var clash = await _db.AvailabilityOverrides
                    .AnyAsync(o => o.StaffId == staffId && o.Date == command.Date && o.IsUnavailable, cancellationToken)
                    .ConfigureAwait(false);
                if (clash) return Result.Failure<Response>(StaffErrors.OverrideConflict);
            }

            Result<AvailabilityOverride> created = command.IsUnavailable
                ? AvailabilityOverride.Unavailable(Guid.NewGuid(), tenantId, staffId, command.Date, command.Reason)
                : AvailabilityOverride.Window(Guid.NewGuid(), tenantId, staffId, command.Date, command.StartTime!.Value, command.EndTime!.Value, command.Reason);

            if (created.IsFailure) return Result.Failure<Response>(created.Error);

            _db.AvailabilityOverrides.Add(created.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(new Response(created.Value.Id));
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
            app.MapPost("/staff/{id:guid}/availability-overrides", async (Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? result.ToCreatedResult($"/staff/{id}/availability-overrides/{result.Value.Id}")
                        : result.ToHttpResult();
                })
                .WithName("Staff.AddAvailabilityOverride")
                .WithTags("Staff")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status201Created);
        }
    }
}
