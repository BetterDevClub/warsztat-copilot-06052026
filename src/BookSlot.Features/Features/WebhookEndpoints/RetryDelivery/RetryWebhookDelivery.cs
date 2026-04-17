using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.WebhookEndpoints.RetryDelivery;

/// <summary>Re-queues a failed or exhausted delivery for another attempt.</summary>
public static class RetryWebhookDelivery
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        /// <summary>Resets the delivery to Pending for the worker to pick up.</summary>
        public async Task<Result> HandleAsync(Guid endpointId, Guid deliveryId, CancellationToken cancellationToken)
        {
            var delivery = await _db.WebhookDeliveries
                .FirstOrDefaultAsync(d => d.Id == deliveryId && d.EndpointId == endpointId, cancellationToken)
                .ConfigureAwait(false);

            if (delivery is null)
                return Result.Failure(WebhookEndpointErrors.DeliveryNotFound);

            var result = delivery.RequestRetry(_clock.GetUtcNow());
            if (result.IsFailure) return result;

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
            app.MapPost("/webhook-endpoints/{id:guid}/deliveries/{deliveryId:guid}/retry", async (
                    Guid id, Guid deliveryId, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, deliveryId, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status202Accepted);
                })
                .WithName("WebhookEndpoints.RetryDelivery")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireStaff")
                .Produces(StatusCodes.Status202Accepted)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
