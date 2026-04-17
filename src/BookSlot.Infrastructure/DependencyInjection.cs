using BookSlot.Domain.Abstractions;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Persistence.Interceptors;
using BookSlot.Infrastructure.Security;
using BookSlot.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookSlot.Infrastructure;

/// <summary>Composition root for the persistence + identity + JWT stack.</summary>
public static class DependencyInjection
{
    /// <summary>Connection string key used in <c>appsettings*.json</c>.</summary>
    public const string PostgresConnectionStringName = "Postgres";

    /// <summary>Connection string key for Redis used in <c>appsettings*.json</c>.</summary>
    public const string RedisConnectionStringName = "Redis";

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
            // Tenant query filters capture ICurrentTenant via closure; EF's model-change detector
            // flags this as a pending snapshot diff on every run. The diff is semantic noise, not
            // a real schema change, so we silence the startup warning.
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            options.AddInterceptors(
                provider.GetRequiredService<AuditInterceptor>(),
                provider.GetRequiredService<DomainEventDispatchInterceptor>());
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false; // enforced per-tenant by composite index
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.SignIn.RequireConfirmedEmail = false; // dev default; production flip via configuration
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddHostedService<RoleSeeder>();

        // Redis — IConnectionMultiplexer is a thread-safe singleton; ISlotLock is stateless.
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName)
            ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<ISlotLock, RedisSlotLock>();

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

