using BookSlot.Domain.Abstractions;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure.Persistence;
using BookSlot.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.IntegrationTests.Tenancy;

/// <summary>
/// Exercises <see cref="AppDbContext"/>'s global query filter for
/// <see cref="ITenantScoped"/> entities against a real PostgreSQL container.
/// A test-only entity is defined so we can validate isolation without depending on
/// the real aggregates that will land in later phases.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TenantQueryFilterTests
{
    private readonly PostgresFixture _fixture;

    public TenantQueryFilterTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueryFilter_Hides_Rows_From_Other_Tenants()
    {
        var tenantA = TenantIdFactory.FromSlug("alpha");
        var tenantB = TenantIdFactory.FromSlug("bravo");

        // Seed rows for both tenants using a non-filtering context (tenant unresolved).
        var seedTenant = new CurrentTenantAccessor();
        await using (var seedCtx = new TenantFilterTestDbContext(BuildOptions(), seedTenant))
        {
            await seedCtx.Database.EnsureDeletedAsync();
            await seedCtx.Database.EnsureCreatedAsync();

            seedCtx.Samples.AddRange(
                new TenantSample { Id = Guid.NewGuid(), TenantId = tenantA, Name = "a-1" },
                new TenantSample { Id = Guid.NewGuid(), TenantId = tenantA, Name = "a-2" },
                new TenantSample { Id = Guid.NewGuid(), TenantId = tenantB, Name = "b-1" });
            await seedCtx.SaveChangesAsync();
        }

        // Query as tenant A — should see only A's rows.
        var asA = new CurrentTenantAccessor();
        asA.Set(tenantA, "alpha");
        await using (var ctx = new TenantFilterTestDbContext(BuildOptions(), asA))
        {
            var names = await ctx.Samples.Select(x => x.Name).ToListAsync();
            names.Should().BeEquivalentTo(["a-1", "a-2"]);
        }

        // Query as tenant B — should see only B's rows.
        var asB = new CurrentTenantAccessor();
        asB.Set(tenantB, "bravo");
        await using (var ctx = new TenantFilterTestDbContext(BuildOptions(), asB))
        {
            var names = await ctx.Samples.Select(x => x.Name).ToListAsync();
            names.Should().BeEquivalentTo(["b-1"]);
        }

        // IgnoreQueryFilters bypasses isolation for diagnostics.
        await using (var ctx = new TenantFilterTestDbContext(BuildOptions(), new CurrentTenantAccessor()))
        {
            var total = await ctx.Samples.IgnoreQueryFilters().CountAsync();
            total.Should().Be(3);
        }
    }

    private DbContextOptions<TenantFilterTestDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<TenantFilterTestDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
    }
}

internal sealed class TenantSample : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Test-only DbContext that inherits <see cref="AppDbContext"/> so the global
/// ITenantScoped query filter installed in <see cref="AppDbContext.OnModelCreating"/>
/// is exercised against a real entity.
/// </summary>
internal sealed class TenantFilterTestDbContext : AppDbContext
{
    public TenantFilterTestDbContext(DbContextOptions<TenantFilterTestDbContext> options, ICurrentTenant currentTenant)
        : base(options, currentTenant)
    {
    }

    public DbSet<TenantSample> Samples => Set<TenantSample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSample>(b =>
        {
            b.ToTable("tenant_samples");
            b.HasKey(x => x.Id);
            b.Property(x => x.TenantId).IsRequired();
            b.Property(x => x.Name).IsRequired().HasMaxLength(64);
        });

        // Run base last so the loop in AppDbContext.OnModelCreating picks up TenantSample
        // and attaches the ITenantScoped query filter.
        base.OnModelCreating(modelBuilder);
    }
}
