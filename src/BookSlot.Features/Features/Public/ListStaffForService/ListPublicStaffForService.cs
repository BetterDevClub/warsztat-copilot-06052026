using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Public.ListStaffForService;

/// <summary>Anonymous lookup of staff who can perform a given service. Powers the public booking widget step 2.</summary>
public static class ListPublicStaffForService
{
    /// <summary>Public-safe staff item.</summary>
    public sealed record Item(Guid Id, string DisplayName, string? Title, string? AvatarUrl);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns active staff assigned to the given service type.</summary>
        public async Task<Response> HandleAsync(Guid serviceTypeId, CancellationToken cancellationToken)
        {
            var items = await _db.StaffServiceAssignments.AsNoTracking()
                .Where(a => a.ServiceTypeId == serviceTypeId)
                .Join(_db.Staff.AsNoTracking().Where(s => s.IsActive),
                    a => a.StaffId, s => s.Id,
                    (a, s) => new Item(s.Id, s.DisplayName, s.Title, s.AvatarUrl))
                .OrderBy(s => s.DisplayName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return new Response(items);
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
            app.MapGet("/public/service-types/{serviceTypeId:guid}/staff", async (Guid serviceTypeId, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(serviceTypeId, ct).ConfigureAwait(false);
                    return Results.Ok(result);
                })
                .WithName("Public.ListStaffForService")
                .WithTags("Public")
                .AllowAnonymous()
                .Produces<Response>();
        }
    }
}
