using BookSlot.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// DI + pipeline wire-up for tenant resolution. Hosts call
/// <see cref="AddTenancy(IServiceCollection, IConfiguration)"/> during startup and
/// <see cref="UseTenantResolution(IApplicationBuilder)"/> in the request pipeline before
/// endpoint routing dispatches tenant-scoped endpoints.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenant resolution middleware, scoped <see cref="ICurrentTenant"/>,
    /// <see cref="CurrentTenantAccessor"/>, and binds <see cref="TenantResolutionOptions"/>
    /// from the <c>Tenancy</c> configuration section.
    /// </summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TenantResolutionOptions>()
            .Bind(configuration.GetSection(TenantResolutionOptions.SectionName));

        services.AddScoped<CurrentTenantAccessor>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentTenantAccessor>()));

        services.AddScoped<RequireTenantFilter>();
        services.AddScoped<TenantResolutionMiddleware>();

        return services;
    }

    /// <summary>Adds <see cref="TenantResolutionMiddleware"/> to the pipeline.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
