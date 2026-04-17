using System.Reflection;
using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context for the application. Inherits <see cref="IdentityDbContext{TUser,TRole,TKey}"/>
/// to own ASP.NET Identity tables alongside domain aggregates. Uses snake_case naming
/// conventions, scans <see cref="InfrastructureAssemblyMarker.Assembly"/> for
/// <see cref="IEntityTypeConfiguration{TEntity}"/> implementations, and configures
/// aggregates to ignore <see cref="IDomainEvent"/> collections. Interceptors (audit,
/// domain event dispatch) are registered through DI.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ICurrentTenant _currentTenant;

    /// <summary>Creates the context with the given options and resolved tenant.</summary>
    public AppDbContext(DbContextOptions options, ICurrentTenant currentTenant) : base(options)
    {
        ArgumentNullException.ThrowIfNull(currentTenant);
        _currentTenant = currentTenant;
    }

    /// <summary>Refresh tokens issued to users. Not tenant-filtered — lookup by token hash is authoritative.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>API keys issued by tenant owners. Global query filter enforces cross-tenant isolation.</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>Tenant visible to this context. Captured by global query filters.</summary>
    protected ICurrentTenant CurrentTenant => _currentTenant;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(InfrastructureAssemblyMarker.Assembly);

        // Email uniqueness is per-tenant, not global — one address may exist in multiple tenants.
        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.TenantId).IsRequired();
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
        });

        var applyFilterMethod = typeof(AppDbContext)
            .GetMethod(nameof(ApplyTenantQueryFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entity.ClrType;

            // AggregateRoot<T>.DomainEvents is an in-memory side channel, never persisted.
            if (IsAggregateRoot(clrType))
            {
                modelBuilder.Entity(clrType).Ignore(nameof(AggregateRoot<int>.DomainEvents));
            }

            // Multi-tenant isolation: every ITenantScoped entity gets a global query filter
            // that compares TenantId against the ambient ICurrentTenant.
            if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                applyFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => _currentTenant.TenantId != null && e.TenantId == _currentTenant.TenantId);
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
