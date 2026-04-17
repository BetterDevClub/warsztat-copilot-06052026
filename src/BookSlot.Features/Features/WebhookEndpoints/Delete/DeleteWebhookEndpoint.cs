using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.WebhookEndpoints.Delete;

/// <summary>Deletes a webhook endpoint and cascades to its deliveries.</summary>
public static class DeleteWebhookEndpoint
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db) => _db = db;

        /// <summary>Removes the endpoint.</summary>
        public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
        {
            var endpoint = await _db.WebhookEndpoints
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (endpoint is null)
                return Result.Failure(WebhookEndpointErrors.NotFound);

            _db.WebhookEndpoints.Remove(endpoint);
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
            app.MapDelete("/webhook-endpoints/{id:guid}", async (
                    Guid id, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("WebhookEndpoints.Delete")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireOwner")
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
