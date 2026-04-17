using BookSlot.Features.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.Health.Ping;

/// <summary>
/// Walking-skeleton endpoint. Lets us verify end-to-end that the host boots,
/// endpoint auto-registration works, and Minimal API is wired up. Replace with real
/// health checks in Phase 31.
/// </summary>
public static class Ping
{
    /// <summary>Response payload.</summary>
    /// <param name="Message">Fixed "pong" greeting.</param>
    /// <param name="TimestampUtc">Server time in UTC.</param>
    public sealed record Response(string Message, DateTimeOffset TimestampUtc);

    /// <summary>Handles the request.</summary>
    public static Response Handle() => new("pong", DateTimeOffset.UtcNow);

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapGet("/ping", () => Results.Ok(Handle()))
                .WithName("Ping")
                .WithTags("System")
                .Produces<Response>();
        }
    }
}
