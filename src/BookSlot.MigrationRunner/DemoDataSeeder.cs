using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Tenants;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookSlot.MigrationRunner;

/// <summary>
/// Idempotently seeds a demo tenant and two users (Owner + Staff). Invoked by the
/// <see cref="Program"/> entry point when <c>Seed:Demo</c> is true (default in Development).
/// </summary>
internal static class DemoDataSeeder
{
    private const string DemoTenantSlug = "demo-warsztat";
    private static readonly Guid DemoTenantId = TenantIdFactory.FromSlug(DemoTenantSlug);

    public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Enter the demo tenant scope so the EF query filters resolve correctly when we
        // probe for "already seeded?" idempotency. Tenant rows themselves use
        // IgnoreQueryFilters since the Tenant table itself is not tenant-scoped via filter.
        using var _ = AmbientCurrentTenant.EnterScope(DemoTenantId, DemoTenantSlug);

        await SeedTenantAsync(db, logger, ct).ConfigureAwait(false);
        await SeedUserAsync(userManager, "admin@demo.local", "Admin123!", Roles.Owner, logger).ConfigureAwait(false);
        await SeedUserAsync(userManager, "staff@demo.local", "Staff123!", Roles.Staff, logger).ConfigureAwait(false);
    }

    private static async Task SeedTenantAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        if (await db.Set<Tenant>().IgnoreQueryFilters().AnyAsync(t => t.Id == DemoTenantId, ct).ConfigureAwait(false))
        {
            logger.LogDebug("Demo tenant already exists — skipping.");
            return;
        }

        var slug = TenantSlug.Create("demo-warsztat");
        var tenantResult = Tenant.Create(DemoTenantId, slug.Value, "Demo Warsztat", DateTimeOffset.UtcNow);
        if (tenantResult.IsFailure)
        {
            throw new InvalidOperationException($"Cannot create demo tenant: {tenantResult.Error}");
        }

        db.Set<Tenant>().Add(tenantResult.Value);
        db.TenantSettings.Add(TenantSettings.CreateDefault(DemoTenantId));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Seeded demo tenant 'demo-warsztat' (id: {Id}).", DemoTenantId);
    }

    private static async Task SeedUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role, ILogger logger)
    {
        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is not null)
        {
            logger.LogDebug("User {Email} already exists — skipping.", email);
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = DemoTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var createResult = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to seed user {email}: {errors}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to assign role {role} to {email}: {errors}");
        }

        logger.LogInformation("Seeded user {Email} with role {Role}.", email, role);
    }
}
