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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookSlot.Features.Auth.Refresh;

/// <summary>
/// Rotates a refresh token: the incoming token is revoked, a brand-new access + refresh
/// pair is issued. Public endpoint — tenant context is derived from the stored refresh
/// token row, not from the middleware (no slug header is sent by the client on refresh).
/// </summary>
public static class Refresh
{
    /// <summary>Request body.</summary>
    public sealed record Command(string RefreshToken);

    /// <summary>Response payload.</summary>
    public sealed record Response(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

    /// <summary>FluentValidation rules.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IJwtTokenGenerator _jwt;
        private readonly IOptions<JwtOptions> _jwtOptions;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(
            AppDbContext db,
            UserManager<ApplicationUser> users,
            IJwtTokenGenerator jwt,
            IOptions<JwtOptions> jwtOptions,
            TimeProvider clock)
        {
            _db = db;
            _users = users;
            _jwt = jwt;
            _jwtOptions = jwtOptions;
            _clock = clock;
        }

        /// <summary>Rotates the refresh token and returns a new pair.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var hash = TokenHasher.HashRefreshToken(command.RefreshToken);
            var existing = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
                .ConfigureAwait(false);

            var now = _clock.GetUtcNow();
            if (existing is null || !existing.IsActive(now))
            {
                return Result.Failure<Response>(AuthErrors.InvalidRefreshToken);
            }

            var user = await _users.FindByIdAsync(existing.UserId.ToString()).ConfigureAwait(false);
            if (user is null)
            {
                return Result.Failure<Response>(AuthErrors.InvalidRefreshToken);
            }

            var roles = await _users.GetRolesAsync(user).ConfigureAwait(false);
            var access = _jwt.CreateAccessToken(user, existing.TenantSlug, roles);

            var rawRefresh = TokenHasher.NewOpaqueToken();
            var replacement = new RefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = existing.TenantId,
                TenantSlug = existing.TenantSlug,
                UserId = existing.UserId,
                TokenHash = TokenHasher.HashRefreshToken(rawRefresh),
                CreatedAt = now,
                ExpiresAt = now.Add(_jwtOptions.Value.RefreshTokenLifetime),
            };
            existing.RevokedAt = now;
            existing.ReplacedByTokenHash = replacement.TokenHash;
            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(
                access.Value, access.ExpiresAt,
                rawRefresh, replacement.ExpiresAt));
        }
    }

    /// <summary>Endpoint registration — public (no tenant required).</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.Public;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapPost("/auth/refresh", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Auth.Refresh")
                .WithTags("Auth")
                .WithValidation<Command>()
                .RequireRateLimiting("auth-sensitive")
                .AllowAnonymous()
                .Produces<Response>();
        }
    }
}
