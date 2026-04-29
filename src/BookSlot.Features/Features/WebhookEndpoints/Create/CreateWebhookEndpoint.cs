using System.Security.Cryptography;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.Webhooks;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Domain.Abstractions;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.WebhookEndpoints.Create;

/// <summary>Registers a new webhook endpoint subscription for the current tenant.</summary>
public static class CreateWebhookEndpoint
{
    /// <summary>Request body.</summary>
    /// <param name="Url">Absolute http(s) URL of the subscriber.</param>
    /// <param name="SubscribedEvents">Event types to subscribe to.</param>
    /// <param name="Description">Optional human-readable description.</param>
    public sealed record Command(
        string Url,
        IReadOnlyList<string> SubscribedEvents,
        string? Description);

    /// <summary>Response — includes the generated signing secret (shown only once).</summary>
    public sealed record Response(
        Guid Id,
        string Url,
        IReadOnlyList<string> SubscribedEvents,
        string? Description,
        bool IsActive,
        string Secret);

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
        private readonly ICurrentTenant _tenant;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
        {
            _db = db;
            _tenant = tenant;
            _clock = clock;
        }

        /// <summary>Creates the endpoint.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            if (_tenant.TenantId is null)
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));

            var secret = GenerateSecret();
            var result = WebhookEndpoint.Create(
                Guid.NewGuid(),
                _tenant.TenantId.Value,
                command.Url,
                secret,
                command.SubscribedEvents,
                command.Description,
                _clock.GetUtcNow());

            if (result.IsFailure)
                return Result.Failure<Response>(result.Error);

            _db.WebhookEndpoints.Add(result.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var e = result.Value;
            return Result.Success(new Response(
                e.Id, e.Url, e.SubscribedEvents, e.Description, e.IsActive, secret));
        }

        // 32-byte secret, url-safe base64 — presented once and never retrievable again.
        private static string GenerateSecret()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
            app.MapPost("/webhook-endpoints", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var validation = await new Validator().ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(validation.Errors.GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(successStatus: StatusCodes.Status201Created);
                })
                .WithName("WebhookEndpoints.Create")
                .WithTags("Webhook Endpoints")
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status201Created)
                .ProducesValidationProblem();
        }
    }
}
