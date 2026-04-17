using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.RemoveAvailabilityOverride;

/// <summary>Removes an availability override. Owner only.</summary>
public static class RemoveAvailabilityOverride
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Deletes the override if it belongs to the given staff member.</summary>
        public async Task<Result> HandleAsync(Guid staffId, Guid overrideId, CancellationToken cancellationToken)
        {
            var entity = await _db.AvailabilityOverrides
                .FirstOrDefaultAsync(o => o.Id == overrideId && o.StaffId == staffId, cancellationToken)
                .ConfigureAwait(false);
            if (entity is null) return Result.Failure(StaffErrors.OverrideNotFound);
            _db.AvailabilityOverrides.Remove(entity);
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
            app.MapDelete("/staff/{id:guid}/availability-overrides/{overrideId:guid}", async (Guid id, Guid overrideId, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, overrideId, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Staff.RemoveAvailabilityOverride")
                .WithTags("Staff")
                .RequireAuthorization("RequireOwner");
        }
    }
}
