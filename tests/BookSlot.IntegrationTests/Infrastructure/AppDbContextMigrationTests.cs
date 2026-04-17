using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.IntegrationTests.Infrastructure;

/// <summary>
/// Smoke-tests the Phase 4 persistence skeleton: migrations apply against a real Postgres
/// container and <see cref="AppDbContext"/> resolves from DI. No entities exist yet, so the
/// initial migration is empty — we assert it runs cleanly end-to-end.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AppDbContextMigrationTests
{
    private readonly PostgresFixture _fixture;

    /// <summary>Creates a new test instance.</summary>
    public AppDbContextMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrate_ShouldApplyInitialMigration_AgainstFreshContainer()
    {
        await using var context = await _fixture.CreateMigratedContextAsync();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        applied.Should().NotBeEmpty("the InitialCreate migration must have been applied");
        applied.Should().Contain(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppDbContext_ShouldUseSnakeCaseNamingConvention_ForMigrationsHistory()
    {
        await using var context = await _fixture.CreateMigratedContextAsync();

        // Naming convention takes effect on the __EFMigrationsHistory table by applying
        // snake_case to the default history schema name ("__ef_migrations_history").
        var count = await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM __ef_migrations_history")
            .ToListAsync();

        count.Should().ContainSingle().Which.Should().BeGreaterThan(0);
    }
}
