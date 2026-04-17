namespace BookSlot.Features.Shared.Endpoints;

/// <summary>
/// Declares which route group an <see cref="IEndpoint"/> belongs to.
/// The host wires one route group per scope (public root vs. the tenant-scoped
/// <c>/api/v1</c> group) and applies different endpoint filters to each.
/// </summary>
public enum EndpointScope
{
    /// <summary>No tenant required. Mounted on the application root.</summary>
    Public = 0,

    /// <summary>Requires a resolved tenant. Mounted on <c>/api/v1</c>.</summary>
    TenantScoped = 1,
}
