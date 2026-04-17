using BookSlot.Domain.Primitives;

namespace BookSlot.Features.WebhookEndpoints;

/// <summary>Feature-layer errors for the WebhookEndpoints group.</summary>
internal static class WebhookEndpointErrors
{
    internal static readonly Error NotFound =
        Error.NotFound("WebhookEndpoint.NotFound", "Webhook endpoint not found.");

    internal static readonly Error DeliveryNotFound =
        Error.NotFound("WebhookDelivery.NotFound", "Webhook delivery not found.");
}
