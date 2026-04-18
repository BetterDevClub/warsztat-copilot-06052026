using BookSlot.Domain.Abstractions;
using BookSlot.Infrastructure;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookSlot.MigrationRunner;

/// <summary>
/// Standalone runner that owns database lifecycle:
/// <list type="number">
///   <item>Apply EF Core migrations against the configured Postgres instance.</item>
///   <item>Seed Identity roles (Owner / Staff / Viewer).</item>
///   <item>Optionally seed demo tenant + users when <c>Seed:Demo</c> is true (default in Development).</item>
/// </list>
/// Hosts (Api / Web / Worker) NEVER migrate or seed at startup; they assume the schema
/// is already in place. Run this project once before booting any host:
/// <code>dotnet run --project src/BookSlot.MigrationRunner</code>
/// CLI flags: <c>--seed-demo</c> / <c>--no-seed-demo</c> override <c>Seed:Demo</c>.
/// Exit codes: <c>0</c> = success, <c>1</c> = failure.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddEnvironmentVariables();

        // Only register persistence — no Redis, notifications, integrations or HTTP clients.
        builder.Services.AddPersistence(builder.Configuration);

        // EF tenant query filters require an ICurrentTenant. The demo seeder briefly enters
        // the demo tenant scope when reading; for everything else "unresolved" is correct.
        builder.Services.AddSingleton<ICurrentTenant, AmbientCurrentTenant>();
        builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();
        // SignInManager needs IAuthenticationSchemeProvider; DataProtection needed by token providers.
        builder.Services.AddAuthentication();
        builder.Services.AddDataProtection();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<MigrationRunnerMarker>>();

        var seedDemo = ResolveSeedDemoFlag(args, builder.Configuration, builder.Environment);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                logger.LogInformation("Applying EF Core migrations to '{Database}' on '{Host}'...",
                    db.Database.GetDbConnection().Database,
                    db.Database.GetDbConnection().DataSource);

                var pending = (await db.Database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("No pending migrations — schema is up to date.");
                }
                else
                {
                    logger.LogInformation("Pending migrations ({Count}): {Names}", pending.Count, string.Join(", ", pending));
                    await db.Database.MigrateAsync().ConfigureAwait(false);
                    logger.LogInformation("Migrations applied successfully.");
                }
            }

            logger.LogInformation("Seeding Identity roles...");
            await RoleSeeder.EnsureRolesAsync(host.Services, logger).ConfigureAwait(false);

            if (seedDemo)
            {
                logger.LogInformation("Seeding demo tenant + users (Seed:Demo=true)...");
                await DemoDataSeeder.SeedAsync(host.Services, logger, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                logger.LogInformation("Skipping demo data seeding (Seed:Demo=false).");
            }

            logger.LogInformation("MigrationRunner completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "MigrationRunner failed.");
            return 1;
        }
    }

    private static bool ResolveSeedDemoFlag(string[] args, IConfiguration configuration, IHostEnvironment env)
    {
        if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (args.Contains("--no-seed-demo", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var fromConfig = configuration.GetValue<bool?>("Seed:Demo");
        return fromConfig ?? env.IsDevelopment();
    }

    private sealed class MigrationRunnerMarker;
}
