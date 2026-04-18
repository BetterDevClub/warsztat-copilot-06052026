using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BookSlot.Infrastructure.Security;

/// <summary>
/// CORS for the BookSlot API. Browser callers (e.g. embedded booking widgets) must
/// be explicitly listed in <c>Cors:AllowedOrigins</c>. In Development the policy
/// also accepts <c>http://localhost</c> and <c>https://localhost</c> on any port so
/// the Blazor dev server and curl-from-browser sessions just work.
/// </summary>
public static class CorsExtensions
{
    public const string PolicyName = "BookSlotDefault";
    private const string ConfigSection = "Cors:AllowedOrigins";

    public static IServiceCollection AddBookSlotCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var origins = configuration.GetSection(ConfigSection).Get<string[]>() ?? Array.Empty<string>();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                if (origins.Length > 0)
                {
                    policy.WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .WithExposedHeaders("X-Correlation-Id");
                }

                if (environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(origin =>
                        Uri.TryCreate(origin, UriKind.Absolute, out var u) &&
                        (u.Host is "localhost" or "127.0.0.1"))
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .WithExposedHeaders("X-Correlation-Id");
                }
            });
        });

        return services;
    }

    public static IApplicationBuilder UseBookSlotCors(this IApplicationBuilder app)
        => app.UseCors(PolicyName);
}
