using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BookSlot.Features.ApiKeys.Create;

/// <summary>
/// Issues a new API key for the current tenant. The raw secret is returned exactly once
/// in the response; the server persists only its HMAC. Restricted to Owner role.
/// </summary>
public static class CreateApiKey
{
    /// <summary>Public prefix for every key. Kept human-readable for log forensics.</summary>
    public const string KeyPrefix = "bk_";

    /// <summary>Request body.</summary>
    public sealed record Command(string Name);

    /// <summary>Response returned only at creation.</summary>
    public sealed record Response(Guid Id, string Name, string PlainTextKey, string Prefix, DateTimeOffset CreatedAt);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;
        private readonly ICurrentUser _user;
        private readonly IOptions<JwtOptions> _jwtOptions;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant, ICurrentUser user, IOptions<JwtOptions> jwtOptions, TimeProvider clock)
        {
            _db = db;
            _tenant = tenant;
            _user = user;
            _jwtOptions = jwtOptions;
            _clock = clock;
        }

        /// <summary>Creates and persists the key.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (_user.UserId is null)
            {
                return Result.Failure<Response>(ApiKeyErrors.Unauthenticated);
            }

            if (_tenant.TenantId is null)
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));

            var secret = TokenHasher.NewOpaqueToken(24);
            var prefix = KeyPrefix + TokenHasher.NewOpaqueToken(6);
            var plainText = $"{prefix}.{secret}";
            var hash = TokenHasher.HmacApiKey(secret, _jwtOptions.Value.ApiKeyPepper);

            var key = new ApiKey
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.TenantId.Value,
                Name = command.Name,
                Prefix = prefix,
                KeyHash = hash,
                CreatedAt = _clock.GetUtcNow(),
                CreatedByUserId = _user.UserId.Value,
            };
            _db.ApiKeys.Add(key);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(key.Id, key.Name, plainText, key.Prefix, key.CreatedAt));
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

            app.MapPost("/api-keys", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status201Created);
                })
                .WithName("ApiKeys.Create")
                .WithTags("ApiKeys")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status201Created);
        }
    }
}
