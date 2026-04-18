using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Public.ListServiceTypes;

/// <summary>Anonymous lookup of active service types for the current tenant. Powers the public booking widget.</summary>
public static class ListPublicServiceTypes
{
    /// <summary>Public-safe service type item.</summary>
    public sealed record Item(
        Guid Id,
        string Name,
        string Slug,
        string? Description,
        int DurationMinutes,
        decimal Price,
        string Currency);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns active services for the current (resolved) tenant.</summary>
        public async Task<Response> HandleAsync(CancellationToken cancellationToken)
        {
            var items = await _db.ServiceTypes.AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new Item(s.Id, s.Name, s.Slug, s.Description, s.DurationMinutes, s.Price, s.Currency))
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
            app.MapGet("/public/service-types", async (Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(ct).ConfigureAwait(false);
                    return Results.Ok(result);
                })
                .WithName("Public.ListServiceTypes")
                .WithTags("Public")
                .AllowAnonymous()
                .Produces<Response>();
        }
    }
}
