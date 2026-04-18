using BookSlot.Domain.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Idempotently seeds the global Identity roles (<c>Owner</c>, <c>Staff</c>, <c>Viewer</c>).
/// Roles are not per-tenant; a single row per role is sufficient and is referenced by every
/// <see cref="ApplicationUser"/>. Invoked once by <c>BookSlot.MigrationRunner</c>;
/// hosts no longer auto-seed at startup.
/// </summary>
public static class RoleSeeder
{
    /// <summary>
    /// Ensures every role in <see cref="Roles.All"/> exists. Throws on identity failure
    /// so the caller (MigrationRunner) can surface a non-zero exit code.
    /// </summary>
    public static async Task EnsureRolesAsync(IServiceProvider services, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var role in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await roleManager.RoleExistsAsync(role).ConfigureAwait(false))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new ApplicationRole { Name = role }).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to seed role '{role}': {errors}");
            }

            logger?.LogInformation("Seeded role {Role}.", role);
        }
    }
}
