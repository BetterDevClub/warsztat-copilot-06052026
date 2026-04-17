using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Staff;

/// <summary>
/// Junction entity: which services a staff member can perform. Tenant-scoped because both
/// sides are tenant-scoped; keeping <see cref="TenantId"/> denormalised here lets the global
/// query filter short-circuit cross-tenant leaks.
/// </summary>
public sealed class StaffServiceAssignment : Entity<Guid>, ITenantScoped
{
    private StaffServiceAssignment() { }

    /// <summary>Creates a new assignment.</summary>
    public StaffServiceAssignment(Guid id, Guid tenantId, Guid staffId, Guid serviceTypeId) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        ServiceTypeId = serviceTypeId;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>FK to the staff member.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>FK to the service type.</summary>
    public Guid ServiceTypeId { get; private set; }
}
