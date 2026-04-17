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

namespace BookSlot.Features.Staff.SetAvailabilityRules;

/// <summary>Replaces the full weekly availability ruleset for a staff member. Owner only.</summary>
public static class SetAvailabilityRules
{
    /// <summary>A single rule window.</summary>
    public sealed record RuleDto(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);

    /// <summary>Request body.</summary>
    public sealed record Command(IReadOnlyList<RuleDto> Rules);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Rules).NotNull();
            RuleForEach(x => x.Rules).ChildRules(r =>
            {
                r.RuleFor(x => x.DayOfWeek).IsInEnum();
                r.RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime);
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

        /// <summary>Loads staff, wipes existing rules, inserts new ones.</summary>
        public async Task<Result> HandleAsync(Guid staffId, Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            var tenantId = _tenant.TenantId!.Value;

            var staffExists = await _db.Staff.AnyAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
            if (!staffExists) return Result.Failure(StaffErrors.NotFound);

            var existing = await _db.AvailabilityRules
                .Where(r => r.StaffId == staffId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            _db.AvailabilityRules.RemoveRange(existing);

            foreach (var dto in command.Rules)
            {
                var created = AvailabilityRule.Create(Guid.NewGuid(), tenantId, staffId, dto.DayOfWeek, dto.StartTime, dto.EndTime);
                if (created.IsFailure) return Result.Failure(created.Error);
                _db.AvailabilityRules.Add(created.Value);
            }

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
            app.MapPut("/staff/{id:guid}/availability-rules", async (Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Staff.SetAvailabilityRules")
                .WithTags("Staff")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
