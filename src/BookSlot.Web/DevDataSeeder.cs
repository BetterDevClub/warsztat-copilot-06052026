using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Tenants;
using BookSlot.Domain.ValueObjects;
using BookSlot.Infrastructure.Identity;
using BookSlot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Web;

/// <summary>
/// Seeds a demo tenant and two users (Owner + Staff) in Development.
/// Idempotent — skips creation when the tenant/user already exists.
/// Registered only when the environment is Development.
/// </summary>
internal sealed class DevDataSeeder : IHostedService
{
    // Deterministic GUIDs so the seeder is truly idempotent across restarts.
    private static readonly Guid DemoTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevDataSeeder> _logger;

    public DevDataSeeder(IServiceScopeFactory scopeFactory, ILogger<DevDataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await SeedTenantAsync(db, cancellationToken).ConfigureAwait(false);
            await SeedUserAsync(userManager, "admin@demo.local", "Admin123!", Roles.Owner).ConfigureAwait(false);
            await SeedUserAsync(userManager, "staff@demo.local", "Staff123!", Roles.Staff).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dev data seeding failed — database may not be ready.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedTenantAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Set<Tenant>().AnyAsync(t => t.Id == DemoTenantId, ct).ConfigureAwait(false))
        {
            _logger.LogDebug("Demo tenant already exists — skipping.");
            return;
        }

        var slug = TenantSlug.Create("demo-warsztat");
        var tenantResult = Tenant.Create(DemoTenantId, slug.Value, "Demo Warsztat", DateTimeOffset.UtcNow);
        if (tenantResult.IsFailure)
        {
            _logger.LogError("Cannot create demo tenant: {Error}", tenantResult.Error);
            return;
        }

        db.Set<Tenant>().Add(tenantResult.Value);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Seeded demo tenant 'demo-warsztat' (id: {Id}).", DemoTenantId);
    }

    private async Task SeedUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string role)
    {
        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogDebug("User {Email} already exists — skipping.", email);
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

        var result = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to seed user {Email}: {Errors}", email, string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);
        _logger.LogInformation("Seeded user {Email} with role {Role}.", email, role);
    }
}
