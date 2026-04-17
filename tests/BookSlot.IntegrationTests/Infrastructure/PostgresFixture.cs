using BookSlot.Infrastructure;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BookSlot.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up an ephemeral PostgreSQL container for a test collection, exposes its connection
/// string, and tears the container down when tests finish. Tests share one container per
/// collection to amortize the ~3s startup cost.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("bookslot_test")
        .WithUsername("bookslot")
        .WithPassword("bookslot")
        .Build();

    /// <summary>Connection string to the running Postgres container.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Builds a fresh <see cref="ServiceProvider"/> wired through <see cref="DependencyInjection.AddInfrastructure"/>,
    /// using the container's connection string. Each test should dispose the provider (or use
    /// the scoped context helper) to release the scoped <see cref="AppDbContext"/>.
    /// </summary>
    public ServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.PostgresConnectionStringName}"] = ConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<BookSlot.Domain.Abstractions.ICurrentUser, FakeCurrentUser>();
        services.AddSingleton<BookSlot.Domain.Abstractions.ICurrentTenant, FakeCurrentTenant>();
        services.AddInfrastructure(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Convenience helper: build a service provider, open a scope, resolve and migrate.</summary>
    public async Task<AppDbContext> CreateMigratedContextAsync(CancellationToken cancellationToken = default)
    {
        var provider = BuildServices();
        var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>xUnit collection binding so tests can inject the shared fixture.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>Collection name referenced by <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Postgres";
}

internal sealed class FakeCurrentUser : BookSlot.Domain.Abstractions.ICurrentUser
{
    public bool IsAuthenticated => false;
    public Guid? UserId => null;
    public string? Email => null;
    public IReadOnlyCollection<string> Roles { get; } = [];
    public bool IsInRole(string role) => false;
}

internal sealed class FakeCurrentTenant : BookSlot.Domain.Abstractions.ICurrentTenant
{
    public bool IsResolved => false;
    public Guid? TenantId => null;
    public string? Slug => null;
}
