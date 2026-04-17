using BookSlot.Domain.Primitives;
using BookSlot.Domain.Webhooks;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.WebhookEndpoints.GetDeliveries;

/// <summary>Returns recent deliveries for a given endpoint for operational debugging.</summary>
public static class GetWebhookDeliveries
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    /// <summary>List item.</summary>
    public sealed record Item(
        Guid Id,
        string EventType,
        string Status,
        int? LastStatusCode,
        int AttemptCount,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? LastAttemptAt,
        DateTimeOffset CreatedAt);

    /// <summary>Envelope.</summary>
    public sealed record Response(
        IReadOnlyList<Item> Items,
        int Page,
        int PageSize,
        int TotalCount);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Returns a paginated slice of deliveries.</summary>
        public async Task<Result<Response>> HandleAsync(
            Guid endpointId,
            WebhookDeliveryStatus? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var endpointExists = await _db.WebhookEndpoints.AsNoTracking()
                .AnyAsync(e => e.Id == endpointId, cancellationToken)
                .ConfigureAwait(false);
            if (!endpointExists)
                return Result.Failure<Response>(WebhookEndpointErrors.NotFound);

            var effectivePage = page < 1 ? 1 : page;
            var effectivePageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

            var query = _db.WebhookDeliveries.AsNoTracking()
                .Where(d => d.EndpointId == endpointId);
            if (status.HasValue) query = query.Where(d => d.Status == status.Value);

            var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .Select(d => new Item(
                    d.Id, d.EventType, d.Status.ToString(),
                    d.LastStatusCode, d.AttemptCount, d.NextAttemptAt, d.LastAttemptAt, d.CreatedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Success(new Response(items, effectivePage, effectivePageSize, total));
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
            app.MapGet("/webhook-endpoints/{id:guid}/deliveries", async (
                    Guid id,
                    WebhookDeliveryStatus? status,
                    int? page,
                    int? pageSize,
                    Handler handler,
                    CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(
                        id, status, page ?? 1, pageSize ?? DefaultPageSize, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("WebhookEndpoints.GetDeliveries")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireViewer")
                .Produces<Response>()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
