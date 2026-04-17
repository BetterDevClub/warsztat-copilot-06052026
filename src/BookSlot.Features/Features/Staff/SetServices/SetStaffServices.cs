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

namespace BookSlot.Features.Staff.SetServices;

/// <summary>Replaces the full set of services a staff member can perform. Owner only.</summary>
public static class SetStaffServices
{
    /// <summary>Request body — full replacement, not a patch.</summary>
    public sealed record Command(IReadOnlyList<Guid> ServiceTypeIds);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.ServiceTypeIds).NotNull();
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant) { _db = db; _tenant = tenant; }

        /// <summary>Loads the staff member, validates service ids, replaces assignments.</summary>
        public async Task<Result> HandleAsync(Guid staffId, Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            var tenantId = _tenant.TenantId!.Value;

            var staffExists = await _db.Staff.AnyAsync(s => s.Id == staffId, cancellationToken).ConfigureAwait(false);
            if (!staffExists) return Result.Failure(StaffErrors.NotFound);

            var requestedIds = command.ServiceTypeIds.Distinct().ToList();
            if (requestedIds.Count > 0)
            {
                var foundCount = await _db.ServiceTypes
                    .CountAsync(s => requestedIds.Contains(s.Id), cancellationToken)
                    .ConfigureAwait(false);
                if (foundCount != requestedIds.Count)
                {
                    return Result.Failure(StaffErrors.ServiceTypesNotFound);
                }
            }

            var existing = await _db.StaffServiceAssignments
                .Where(a => a.StaffId == staffId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            _db.StaffServiceAssignments.RemoveRange(existing);

            foreach (var serviceTypeId in requestedIds)
            {
                _db.StaffServiceAssignments.Add(new StaffServiceAssignment(Guid.NewGuid(), tenantId, staffId, serviceTypeId));
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
            app.MapPut("/staff/{id:guid}/services", async (Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Staff.SetServices")
                .WithTags("Staff")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
