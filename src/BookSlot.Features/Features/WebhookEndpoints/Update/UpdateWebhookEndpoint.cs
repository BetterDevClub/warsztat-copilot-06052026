using BookSlot.Domain.Primitives;
using BookSlot.Domain.Webhooks;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.WebhookEndpoints.Update;

/// <summary>Updates an existing webhook endpoint (URL, events, description, active flag).</summary>
public static class UpdateWebhookEndpoint
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        string Url,
        IReadOnlyList<string> SubscribedEvents,
        string? Description,
        bool IsActive);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.Url).NotEmpty().MaximumLength(WebhookEndpoint.MaxUrlLength);
            RuleFor(c => c.SubscribedEvents).NotEmpty();
            RuleFor(c => c.Description).MaximumLength(WebhookEndpoint.MaxDescriptionLength)
                .When(c => c.Description is not null);
        }
    }

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

        /// <summary>Applies the update.</summary>
        public async Task<Result> HandleAsync(Guid id, Command command, CancellationToken cancellationToken)
        {
            var endpoint = await _db.WebhookEndpoints
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (endpoint is null)
                return Result.Failure(WebhookEndpointErrors.NotFound);

            var result = endpoint.Update(
                command.Url, command.SubscribedEvents, command.Description,
                command.IsActive, _clock.GetUtcNow());
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
            app.MapPut("/webhook-endpoints/{id:guid}", async (
                    Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var validation = await new Validator().ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status204NoContent);
                })
                .WithName("WebhookEndpoints.Update")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireOwner")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
