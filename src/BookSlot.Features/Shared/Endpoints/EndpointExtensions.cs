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
    /// Resolves all registered <see cref="IEndpoint"/> instances and calls
    /// <see cref="IEndpoint.MapEndpoint"/> on each.
    /// </summary>
    public static IApplicationBuilder MapEndpoints(
        this WebApplication app,
        RouteGroupBuilder? routeGroupBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();
        IEndpointRouteBuilder builder = routeGroupBuilder is null ? app : routeGroupBuilder;

        foreach (var endpoint in endpoints)
        {
            endpoint.MapEndpoint(builder);
        }

        return app;
    }
}
