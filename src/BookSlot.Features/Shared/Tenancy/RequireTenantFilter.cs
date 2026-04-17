using BookSlot.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Endpoint filter that short-circuits the pipeline with <c>400 Bad Request</c> when no
/// tenant has been resolved for the current request. Applied globally to the tenant
/// route group (<c>/api/v1</c>) in the host's pipeline configuration.
/// </summary>
public sealed class RequireTenantFilter : IEndpointFilter
{
    private readonly ICurrentTenant _currentTenant;

    /// <summary>Creates the filter; <paramref name="currentTenant"/> is resolved per scope.</summary>
    public RequireTenantFilter(ICurrentTenant currentTenant)
    {
        ArgumentNullException.ThrowIfNull(currentTenant);
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!_currentTenant.IsResolved)
        {
            return Results.Problem(
                title: "Tenant not resolved",
                detail: "The request did not identify a tenant. Provide the tenant slug via subdomain, JWT claim, or the X-Tenant-Slug header.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}
