using BookSlot.Domain.Abstractions;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookSlot.Infrastructure;

/// <summary>Composition root for the persistence stack.</summary>
public static class DependencyInjection
{
    /// <summary>Connection string key used in <c>appsettings*.json</c>.</summary>
    public const string PostgresConnectionStringName = "Postgres";

    /// <summary>
    /// Registers <see cref="Persistence.AppDbContext"/> against PostgreSQL with snake_case
    /// naming conventions, the audit + domain-event-dispatch interceptors, and the default
    /// in-process dispatcher. Expects a connection string named <c>Postgres</c>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionStringName}' is missing; configure it in appsettings or user secrets.");

        services.TryAddTimeProvider();
        services.AddSingleton<IDomainEventDispatcher, LoggingDomainEventDispatcher>();
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(InfrastructureAssemblyMarker.Assembly.FullName);
                    npgsql.MigrationsHistoryTable("__ef_migrations_history");
                });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(
                provider.GetRequiredService<AuditInterceptor>(),
                provider.GetRequiredService<DomainEventDispatchInterceptor>());
        });

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
