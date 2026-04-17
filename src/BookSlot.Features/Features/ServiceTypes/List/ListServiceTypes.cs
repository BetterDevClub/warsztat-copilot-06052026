using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.List;

/// <summary>Lists service types for the current tenant with an optional active-only filter.</summary>
public static class ListServiceTypes
{
    /// <summary>Lightweight list item.</summary>
    public sealed record Item(
        Guid Id,
        string Name,
        string Slug,
        int DurationMinutes,
        decimal Price,
        string Currency,
        bool IsActive);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns services for the current tenant; by default only active.</summary>
        public async Task<Result<Response>> HandleAsync(bool includeInactive, CancellationToken cancellationToken)
        {
            var query = _db.ServiceTypes.AsNoTracking();
            if (!includeInactive)
            {
                query = query.Where(s => s.IsActive);
            }

            var items = await query
                .OrderBy(s => s.Name)
                .Select(s => new Item(s.Id, s.Name, s.Slug, s.DurationMinutes, s.Price, s.Currency, s.IsActive))
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

            app.MapGet("/service-types", async (bool? includeInactive, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(includeInactive ?? false, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("ServiceTypes.List")
                .WithTags("ServiceTypes")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
