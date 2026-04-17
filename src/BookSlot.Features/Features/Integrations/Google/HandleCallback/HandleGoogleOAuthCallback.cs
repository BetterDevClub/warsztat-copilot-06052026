using System.Text;
using BookSlot.Domain.Integrations;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Integrations.Google.HandleCallback;

/// <summary>
/// OAuth2 callback target registered with Google. Parses <c>state</c> to recover
/// the originating tenant, then persists an <see cref="IntegrationConnection"/>
/// with the authorization <c>code</c> stashed in <c>RefreshToken</c> as a
/// placeholder. Full token exchange (exchanging code for access+refresh tokens)
/// lands in Phase 22 together with the Google Calendar sync worker — at that
/// point this slice will call Google's token endpoint inline.
/// </summary>
public static class HandleGoogleOAuthCallback
{
    /// <summary>Response body.</summary>
    public sealed record Response(Guid ConnectionId, Guid TenantId);

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

        /// <summary>Validates the callback and persists the connection.</summary>
        public async Task<Result<Response>> HandleAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(error))
                return Result.Failure<Response>(Error.Validation("Google.OAuthError", error));

            if (string.IsNullOrWhiteSpace(code))
                return Result.Failure<Response>(Error.Validation("Google.MissingCode", "Authorization code is required."));

            if (!TryDecodeState(state, out var tenantId))
                return Result.Failure<Response>(Error.Validation("Google.InvalidState", "OAuth state token is invalid."));

            var tenantExists = await _db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Id == tenantId && t.IsActive, cancellationToken).ConfigureAwait(false);
            if (!tenantExists)
                return Result.Failure<Response>(Error.NotFound("Google.TenantNotFound", "Tenant referenced by OAuth state does not exist."));

            // Retire any existing active connection for the tenant before creating a new one.
            var now = _clock.GetUtcNow();
            var existing = await _db.IntegrationConnections.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId
                         && c.Provider == MeetingProvider.GoogleMeet
                         && c.StaffId == null
                         && c.IsActive)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in existing) row.Deactivate(now);

            // Placeholder: store the authorization code as refresh token until Phase 22
            // performs the real token-endpoint exchange.
            var connection = IntegrationConnection.Create(
                Guid.NewGuid(), tenantId, MeetingProvider.GoogleMeet, staffId: null,
                externalAccountId: "pending-exchange",
                accessToken: null,
                refreshToken: code,
                accessTokenExpiresAt: null,
                scope: null,
                now);
            if (connection.IsFailure)
                return Result.Failure<Response>(connection.Error);

            _db.IntegrationConnections.Add(connection.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(connection.Value.Id, tenantId));
        }

        private static bool TryDecodeState(string? state, out Guid tenantId)
        {
            tenantId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(state)) return false;

            try
            {
                var padded = state.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                var pipe = decoded.IndexOf('|', StringComparison.Ordinal);
                if (pipe <= 0) return false;
                return Guid.TryParseExact(decoded[..pipe], "N", out tenantId);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.Public;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapGet("/integrations/google/callback", async (
                    string? code, string? state, string? error, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(code, state, error, ct).ConfigureAwait(false);
                    return result.ToHttpResult();
                })
                .WithName("Integrations.Google.HandleCallback")
                .WithTags("Integrations")
                .AllowAnonymous()
                .Produces<Response>()
                .ProducesValidationProblem()
                .Produces(StatusCodes.Status404NotFound);
        }
    }
}
