using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.WebhookEndpoints.List;

/// <summary>Lists all webhook endpoints in the current tenant. The secret is never returned.</summary>
public static class ListWebhookEndpoints
{
    /// <summary>List item (no secret).</summary>
    public sealed record Item(
        Guid Id,
        string Url,
        IReadOnlyList<string> SubscribedEvents,
        string? Description,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    /// <summary>Envelope.</summary>
    public sealed record Response(IReadOnlyList<Item> Items);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns all endpoints.</summary>
        public async Task<Result<Response>> HandleAsync(CancellationToken cancellationToken)
        {
            var items = await _db.WebhookEndpoints.AsNoTracking()
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new Item(
                    e.Id, e.Url, e.SubscribedEvents, e.Description, e.IsActive, e.CreatedAt, e.UpdatedAt))
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
            app.MapGet("/webhook-endpoints", async (Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("WebhookEndpoints.List")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>();
        }
    }
}
