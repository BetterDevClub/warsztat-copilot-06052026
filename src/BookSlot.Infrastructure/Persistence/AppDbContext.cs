using BookSlot.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context for the application. Uses snake_case naming conventions,
/// scans <see cref="InfrastructureAssemblyMarker.Assembly"/> for <see cref="IEntityTypeConfiguration{TEntity}"/>
/// implementations, and configures aggregates to ignore <see cref="IDomainEvent"/> collections.
/// Interceptors (audit, domain event dispatch) are registered through DI.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context with the given options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(InfrastructureAssemblyMarker.Assembly);

        // AggregateRoot<T>.DomainEvents is an in-memory side channel, never persisted.
        // EF 10 does not discover base generic navigations, but scalars on collections
        // would otherwise fail mapping; ignore at the model level once per aggregate type.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;
            if (IsAggregateRoot(clrType))
            {
                modelBuilder.Entity(clrType).Ignore(nameof(AggregateRoot<int>.DomainEvents));
            }
        }
    }

    private static bool IsAggregateRoot(Type type)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
