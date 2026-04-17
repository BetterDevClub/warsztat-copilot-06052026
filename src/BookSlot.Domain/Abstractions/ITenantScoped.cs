namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Marker for entities that must be isolated per tenant. EF Core applies a global
/// query filter on <see cref="TenantId"/> and the tenant resolution middleware wires
/// the ambient tenant for every request scope.
/// </summary>
public interface ITenantScoped
{
    /// <summary>The owning tenant id. Set once on creation; never mutated.</summary>
    Guid TenantId { get; }
}
