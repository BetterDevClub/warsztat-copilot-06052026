using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ApiKeys.List;

/// <summary>Lists API keys for the current tenant. Raw secrets are never returned.</summary>
public static class ListApiKeys
{
    /// <summary>Single list item — the <c>Prefix</c> is enough to correlate with audit logs.</summary>
    public sealed record Item(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>Enumerates API keys. Relies on the tenant global query filter.</summary>
        public Task<List<Item>> HandleAsync(CancellationToken cancellationToken) =>
            _db.ApiKeys
                .AsNoTracking()
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => new Item(k.Id, k.Name, k.Prefix, k.CreatedAt, k.RevokedAt, k.LastUsedAt))
                .ToListAsync(cancellationToken);
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

            app.MapGet("/api-keys", async (Handler handler, CancellationToken ct) =>
                    Results.Ok(await handler.HandleAsync(ct).ConfigureAwait(false)))
                .WithName("ApiKeys.List")
                .WithTags("ApiKeys")
                .RequireAuthorization("RequireOwner")
                .Produces<List<Item>>();
        }
    }
}
