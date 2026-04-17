using BookSlot.Domain.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Idempotently seeds the global Identity roles (<c>Owner</c>, <c>Staff</c>, <c>Viewer</c>)
/// on application start. Roles are not per-tenant in this system; a single row per role
/// is sufficient and is referenced by every <see cref="ApplicationUser"/>.
/// </summary>
public sealed class RoleSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RoleSeeder> _logger;

    /// <summary>Creates a new seeder.</summary>
    public RoleSeeder(IServiceScopeFactory scopeFactory, ILogger<RoleSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

            foreach (var role in Roles.All)
            {
                if (!await roleManager.RoleExistsAsync(role).ConfigureAwait(false))
                {
                    var result = await roleManager.CreateAsync(new ApplicationRole { Name = role }).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        _logger.LogError(
                            "Failed to seed role {Role}: {Errors}",
                            role,
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                    }
                    else
                    {
                        _logger.LogInformation("Seeded role {Role}.", role);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: database may be unreachable (e.g. test hosts that never migrate,
            // or a dev box where Postgres is down). Don't block host startup over seed data.
            _logger.LogWarning(ex, "Role seeding skipped — database not reachable yet.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
