using Microsoft.Extensions.DependencyInjection;

namespace BookSlot.Features.Shared;

/// <summary>
/// Registers every slice <c>Handler</c> class from the Features assembly as scoped —
/// handlers hold the scoped <c>AppDbContext</c> and are instantiated per request /
/// per circuit. Used by non-API hosts (e.g. Blazor Web host) that want to invoke
/// slice handlers directly via DI without pulling in the JWT bearer stack.
/// </summary>
public static class FeatureHandlerServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = FeaturesAssemblyMarker.Assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsClass: true, IsAbstract: false, Name: "Handler" } &&
                type.IsNested && type.IsNestedPublic)
            {
                services.AddScoped(type);
            }
        }

        return services;
    }
}
