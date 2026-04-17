using System.Security.Cryptography;
using System.Text;
using System.Web;
using BookSlot.Domain.Abstractions;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BookSlot.Features.Integrations.Google.StartOAuth;

/// <summary>
/// Produces the Google OAuth2 consent URL for the current tenant. The admin UI
/// redirects the user's browser to the returned URL; Google then calls the
/// configured <c>RedirectUri</c> (see <see cref="HandleCallback"/>). The
/// generated <c>state</c> carries a signed tenant identifier so the callback can
/// re-attribute the token to the right tenant even though it is anonymous.
/// </summary>
public static class StartGoogleOAuth
{
    /// <summary>Response body.</summary>
    public sealed record Response(string AuthorizationUrl, string State);

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly IOptions<GoogleOAuthOptions> _options;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(IOptions<GoogleOAuthOptions> options, ICurrentTenant tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        /// <summary>Builds the authorization URL and state token.</summary>
        public Response Handle()
        {
            if (_tenant.TenantId is null)
                throw new InvalidOperationException("Current tenant is required for Google OAuth.");

            var opts = _options.Value;
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var state = EncodeState(_tenant.TenantId.Value, nonce);

            var query = new StringBuilder()
                .Append("client_id=").Append(HttpUtility.UrlEncode(opts.ClientId))
                .Append("&redirect_uri=").Append(HttpUtility.UrlEncode(opts.RedirectUri))
                .Append("&response_type=code")
                .Append("&scope=").Append(HttpUtility.UrlEncode(opts.Scopes))
                .Append("&access_type=offline")
                .Append("&prompt=consent")
                .Append("&state=").Append(HttpUtility.UrlEncode(state));

            var url = $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
            return new Response(url, state);
        }

        /// <summary>Encodes the signed state token: base64url(tenantId|nonce).</summary>
        private static string EncodeState(Guid tenantId, string nonce)
        {
            var payload = $"{tenantId:N}|{nonce}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
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
            app.MapGet("/integrations/google/authorize", (Handler handler) => Results.Ok(handler.Handle()))
                .WithName("Integrations.Google.StartOAuth")
                .WithTags("Integrations")
                .RequireAuthorization("RequireOwner")
                .Produces<Response>();
        }
    }
}
