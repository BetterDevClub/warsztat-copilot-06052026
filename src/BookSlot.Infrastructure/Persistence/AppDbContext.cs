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

    /// <summary>Tenants (top-level isolation units). Not tenant-filtered — they ARE the tenants.</summary>
    public DbSet<Domain.Tenants.Tenant> Tenants => Set<Domain.Tenants.Tenant>();

    /// <summary>Per-tenant settings (timezone, branding, booking window). Tenant-filtered via <see cref="ITenantScoped"/>.</summary>
    public DbSet<Domain.Tenants.TenantSettings> TenantSettings => Set<Domain.Tenants.TenantSettings>();

    /// <summary>Bookable services offered by tenants. Tenant-filtered via <see cref="ITenantScoped"/>.</summary>
    public DbSet<Domain.Services.ServiceType> ServiceTypes => Set<Domain.Services.ServiceType>();

    /// <summary>Tenant staff members. Tenant-filtered.</summary>
    public DbSet<Domain.Staff.StaffMember> Staff => Set<Domain.Staff.StaffMember>();

    /// <summary>Junction linking staff to the service types they can perform.</summary>
    public DbSet<Domain.Staff.StaffServiceAssignment> StaffServiceAssignments => Set<Domain.Staff.StaffServiceAssignment>();

    /// <summary>Weekly availability rules per staff member.</summary>
    public DbSet<Domain.Staff.AvailabilityRule> AvailabilityRules => Set<Domain.Staff.AvailabilityRule>();

    /// <summary>One-off availability overrides (holidays, extra hours).</summary>
    public DbSet<Domain.Staff.AvailabilityOverride> AvailabilityOverrides => Set<Domain.Staff.AvailabilityOverride>();

    /// <summary>Short-lived slot holds created during the guest checkout flow.</summary>
    public DbSet<Domain.Reservations.SlotReservation> SlotReservations => Set<Domain.Reservations.SlotReservation>();

    /// <summary>Confirmed and historical appointments.</summary>
    public DbSet<Domain.Bookings.Booking> Bookings => Set<Domain.Bookings.Booking>();

    /// <summary>Templates for recurring booking series.</summary>
    public DbSet<Domain.Bookings.RecurringBooking> RecurringBookings => Set<Domain.Bookings.RecurringBooking>();

    /// <summary>Audit log of notification dispatch attempts (tenant-scoped via global filter).</summary>
    public DbSet<Domain.Notifications.NotificationLog> NotificationLogs => Set<Domain.Notifications.NotificationLog>();

    /// <summary>Subscriber-configured webhook endpoints (tenant-scoped).</summary>
    public DbSet<Domain.Webhooks.WebhookEndpoint> WebhookEndpoints => Set<Domain.Webhooks.WebhookEndpoint>();

    /// <summary>Per-endpoint outbound delivery rows (tenant-scoped).</summary>
    public DbSet<Domain.Webhooks.WebhookDelivery> WebhookDeliveries => Set<Domain.Webhooks.WebhookDelivery>();

    /// <summary>Transactional outbox (not tenant-scoped — worker reads all tenants).</summary>
    public DbSet<Domain.Webhooks.OutboxMessage> OutboxMessages => Set<Domain.Webhooks.OutboxMessage>();

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
