using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookSlot.Features.Shared.Endpoints;

/// <summary>Discovery and registration helpers for <see cref="IEndpoint"/> implementations.</summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Scans the given assemblies for concrete, non-abstract <see cref="IEndpoint"/>
    /// implementations and registers them as transient services in DI.
    /// If no assemblies are given, the <c>BookSlot.Features</c> assembly is scanned.
    /// </summary>
    public static IServiceCollection AddEndpoints(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var scanTargets = assemblies.Length == 0
            ? [FeaturesAssemblyMarker.Assembly]
            : assemblies;

        var endpointTypes = scanTargets
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IEndpoint).IsAssignableFrom(t));

        foreach (var type in endpointTypes)
        {
            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IEndpoint), type));
        }

        return services;
    }

    /// <summary>
    /// Resolves all registered <see cref="IEndpoint"/> instances and dispatches each to
    /// the route builder matching its <see cref="IEndpoint.Scope"/>. Endpoints in
    /// <see cref="EndpointScope.Public"/> are mapped on <paramref name="app"/> (or the
    /// <paramref name="publicGroup"/> if provided); endpoints in
    /// <see cref="EndpointScope.TenantScoped"/> are mapped on <paramref name="tenantGroup"/>.
    /// Tenant-scoped endpoints are silently skipped when no tenant group is supplied —
    /// handy for hosts that do not expose the tenant API surface (e.g. public workers).
    /// </summary>
    public static IApplicationBuilder MapEndpoints(
        this WebApplication app,
        RouteGroupBuilder? publicGroup = null,
        RouteGroupBuilder? tenantGroup = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();
        IEndpointRouteBuilder publicBuilder = publicGroup is null ? app : publicGroup;

        foreach (var endpoint in endpoints)
        {
            switch (endpoint.Scope)
            {
                case EndpointScope.TenantScoped when tenantGroup is not null:
                    endpoint.MapEndpoint(tenantGroup);
                    break;
                case EndpointScope.TenantScoped:
                    // No tenant group wired — skip silently.
                    break;
                case EndpointScope.Public:
                default:
                    endpoint.MapEndpoint(publicBuilder);
                    break;
            }
        }

        return app;
    }
}
