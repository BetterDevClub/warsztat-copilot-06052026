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

namespace BookSlot.Features.ServiceTypes.BulkAssignStaff;

/// <summary>
/// Assigns multiple staff members to a service type in a single request.
/// Already-assigned entries are silently skipped. Owner only.
/// </summary>
public static class BulkAssignStaff
{
    /// <summary>Request body — list of staff member ids to assign to the service type.</summary>
    public sealed record Command(IReadOnlyList<Guid> StaffIds);

    /// <summary>Response payload — number of newly created assignments.</summary>
    public sealed record Response(int AssignedCount);

    /// <summary>Input-level validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.StaffIds).NotNull();
            When(x => x.StaffIds != null, () =>
                RuleForEach(x => x.StaffIds).NotEmpty());
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        /// <summary>
        /// Assigns the requested staff members to the specified service type.
        /// Duplicate requests and already-assigned pairs are silently skipped.
        /// </summary>
        public async Task<Result<Response>> HandleAsync(
            Guid serviceTypeId,
            Command command,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (_tenant.TenantId is null)
            {
                return Result.Failure<Response>(
                    Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            }

            var tenantId = _tenant.TenantId.Value;

            var serviceTypeExists = await _db.ServiceTypes
                .AnyAsync(s => s.Id == serviceTypeId, cancellationToken)
                .ConfigureAwait(false);

            if (!serviceTypeExists)
            {
                return Result.Failure<Response>(ServiceTypeErrors.NotFound);
            }

            if (command.StaffIds == null || command.StaffIds.Count == 0)
            {
                return Result.Success(new Response(0));
            }

            var requestedIds = command.StaffIds.Distinct().ToList();

            var foundCount = await _db.Staff
                .Where(s => requestedIds.Contains(s.Id))
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            if (foundCount != requestedIds.Count)
            {
                return Result.Failure<Response>(ServiceTypeErrors.StaffNotFound);
            }

            var alreadyAssigned = await _db.StaffServiceAssignments
                .Where(a => a.ServiceTypeId == serviceTypeId && requestedIds.Contains(a.StaffId))
                .Select(a => a.StaffId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var toAssign = requestedIds.Except(alreadyAssigned).ToList();

            foreach (var staffId in toAssign)
            {
                _db.StaffServiceAssignments.Add(
                    new StaffServiceAssignment(Guid.NewGuid(), tenantId, staffId, serviceTypeId));
            }

            if (toAssign.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return Result.Success(new Response(toAssign.Count));
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

            app.MapPost(
                    "/service-types/{serviceTypeId:guid}/bulk-assign-staff",
                    async (Guid serviceTypeId, Command command, Handler handler, CancellationToken ct) =>
                    {
                        var result = await handler.HandleAsync(serviceTypeId, command, ct).ConfigureAwait(false);
                        return result.ToHttpResult();
                    })
                .WithName("ServiceTypes.BulkAssignStaff")
                .WithTags("ServiceTypes")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status200OK);
        }
    }
}
