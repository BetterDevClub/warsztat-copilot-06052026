using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Auth.Logout;

/// <summary>
/// Revokes every active refresh token for the authenticated caller. The caller's
/// access token remains valid until it expires (short-lived); client MUST discard it.
/// </summary>
public static class Logout
{
    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _user;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentUser user, TimeProvider clock)
        {
            _db = db;
            _user = user;
            _clock = clock;
        }

        /// <summary>Revokes all active refresh tokens for the caller.</summary>
        public async Task<Result> HandleAsync(CancellationToken cancellationToken)
        {
            if (!_user.IsAuthenticated || _user.UserId is null)
            {
                return Result.Failure(AuthErrors.InvalidCredentials);
            }

            var now = _clock.GetUtcNow();
            var userId = _user.UserId.Value;
            await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
    }

    /// <summary>Endpoint registration — tenant-scoped and authenticated.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapPost("/auth/logout", async (Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Auth.Logout")
                .WithTags("Auth")
                .RequireAuthorization();
        }
    }
}
