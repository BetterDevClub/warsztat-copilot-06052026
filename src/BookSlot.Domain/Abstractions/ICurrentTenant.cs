namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Exposes the tenant resolved for the current unit of work. Implementations live in the
/// host layer (API: resolve by priority: JWT claim &gt; subdomain &gt; explicit header;
/// Worker: set by the job orchestrator when running tenant-scoped work).
/// </summary>
public interface ICurrentTenant
{
    /// <summary>True when a tenant has been resolved for this scope.</summary>
    bool IsResolved { get; }

    /// <summary>Tenant id, or <c>null</c> if unresolved.</summary>
    Guid? TenantId { get; }

    /// <summary>Tenant slug (lowercase subdomain-safe identifier), or <c>null</c> if unresolved.</summary>
    string? Slug { get; }
}
