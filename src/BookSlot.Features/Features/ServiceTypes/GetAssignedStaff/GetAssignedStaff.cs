using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.GetAssignedStaff;

/// <summary>Returns the active staff members currently assigned to a service type.</summary>
public static class GetAssignedStaff
{
    /// <summary>Item in the response list.</summary>
    public sealed record Item(Guid Id, string DisplayName, string? Title);

    /// <summary>Response envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>
        /// Returns active staff assigned to the specified service type within the current tenant.
        /// Tenant isolation is enforced by the global EF query filter on <c>AppDbContext</c>.
        /// </summary>
        public async Task<Result<Response>> HandleAsync(Guid serviceTypeId, CancellationToken cancellationToken)
        {
            var items = await _db.StaffServiceAssignments.AsNoTracking()
                .Where(a => a.ServiceTypeId == serviceTypeId)
                .Join(
                    _db.Staff.AsNoTracking().Where(s => s.IsActive),
                    a => a.StaffId,
                    s => s.Id,
                    (a, s) => new { s.Id, s.DisplayName, s.Title })
                .OrderBy(x => x.DisplayName)
                .Select(x => new Item(x.Id, x.DisplayName, x.Title))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success<Response>(new Response(items));
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

            app.MapGet(
                    "/service-types/{serviceTypeId:guid}/assigned-staff",
                    async (Guid serviceTypeId, Handler handler, CancellationToken ct) =>
                    {
                        var result = await handler.HandleAsync(serviceTypeId, ct).ConfigureAwait(false);
                        return result.ToHttpResult();
                    })
                .WithName("ServiceTypes.GetAssignedStaff")
                .WithTags("ServiceTypes")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
