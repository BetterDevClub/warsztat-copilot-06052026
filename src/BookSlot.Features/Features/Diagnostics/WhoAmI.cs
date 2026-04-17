using BookSlot.Domain.Abstractions;
using BookSlot.Features.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.Diagnostics.WhoAmI;

/// <summary>
/// Tenant-scoped diagnostic slice. Returns the resolved tenant for the request. Useful as
/// a smoke test for the tenant resolution middleware and as a reference for how
/// tenant-scoped endpoints consume <see cref="ICurrentTenant"/>. Will stay behind an
/// admin auth policy once authentication lands in Phase 6.
/// </summary>
public static class WhoAmI
{
    /// <summary>Response payload.</summary>
    public sealed record Response(Guid TenantId, string Slug);

    /// <summary>Pure handler used by the endpoint and by unit tests.</summary>
    public static Response Handle(ICurrentTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new Response(tenant.TenantId!.Value, tenant.Slug!);
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

            app.MapGet("/diagnostics/whoami", (ICurrentTenant tenant) => Results.Ok(Handle(tenant)))
                .WithName("Diagnostics.WhoAmI")
                .WithTags("Diagnostics")
                .Produces<Response>();
        }
    }
}
