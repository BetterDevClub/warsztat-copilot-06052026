using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Integrations;
using BookSlot.Domain.Notifications;
using BookSlot.Domain.Webhooks;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Integrations;
using BookSlot.Infrastructure.Notifications;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Persistence.Interceptors;
using BookSlot.Infrastructure.Security;
using BookSlot.Infrastructure.Services;
using BookSlot.Infrastructure.Webhooks;
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
    /// naming conventions, the audit + domain-event-dispatch interceptors, Identity core +
    /// roles, and JWT options. Used by every host (and by the migration runner) — does NOT
    /// touch Redis, notifications, or webhook integrations.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
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
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            options.AddInterceptors(
                provider.GetRequiredService<AuditInterceptor>(),
                provider.GetRequiredService<DomainEventDispatchInterceptor>());
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.SignIn.RequireConfirmedEmail = false;
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

        return services;
    }

    /// <summary>
    /// Full host registration: persistence + Redis + notifications + integrations.
    /// Hosts that need the complete runtime stack call this; the standalone migration
    /// runner uses <see cref="AddPersistence"/> instead so it can run without Redis.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistence(configuration);

        // Redis — IConnectionMultiplexer is a thread-safe singleton; ISlotLock is stateless.
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName)
            ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<ISlotLock, RedisSlotLock>();

        services.AddNotifications(configuration);
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddIntegrations(configuration);

        return services;
    }

    private static void AddIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GoogleOAuthOptions>()
            .Bind(configuration.GetSection(GoogleOAuthOptions.SectionName));

        // Zoom mock stands in until the real HTTP adapter lands in Phase 22.
        services.AddSingleton<IMeetingLinkGenerator, MockZoomMeetingLinkGenerator>();
    }

    private static void AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<INotificationTemplateRenderer, DefaultNotificationTemplateRenderer>();

        var emailProvider = configuration.GetSection(EmailOptions.SectionName)["Provider"] ?? "Null";
        if (string.Equals(emailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            // SendGrid provider stub routes through NullEmailSender until the adapter is wired
            // (a dedicated HTTP client implementation will land alongside Phase 23 worker jobs).
            services.AddSingleton<IEmailSender, NullEmailSender>();
        }

        var smsProvider = configuration.GetSection(SmsOptions.SectionName)["Provider"] ?? "Null";
        // Twilio adapter deferred; Null sender keeps the abstraction available end-to-end.
        _ = smsProvider;
        services.AddSingleton<ISmsSender, NullSmsSender>();

        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

