using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.List;

/// <summary>Lists staff members. Inactive staff are excluded by default.</summary>
public static class ListStaff
{
    /// <summary>List item.</summary>
    public sealed record Item(Guid Id, string DisplayName, string? Title, string? AvatarUrl, bool IsActive);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns staff for the current tenant.</summary>
        public async Task<Result<Response>> HandleAsync(bool includeInactive, CancellationToken cancellationToken)
        {
            var query = _db.Staff.AsNoTracking();
            if (!includeInactive) query = query.Where(s => s.IsActive);
            var items = await query
                .OrderBy(s => s.DisplayName)
                .Select(s => new Item(s.Id, s.DisplayName, s.Title, s.AvatarUrl, s.IsActive))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(new Response(items));
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
            app.MapGet("/staff", async (bool? includeInactive, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(includeInactive ?? false, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Staff.List")
                .WithTags("Staff")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
