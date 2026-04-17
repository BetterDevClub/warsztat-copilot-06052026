namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Exposes the tenant resolved for the current request. Implementations live in the host
/// (API) and resolve by priority: JWT claim &gt; subdomain &gt; explicit header.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>True when a tenant has been resolved for this request.</summary>
    bool IsResolved { get; }

    /// <summary>Tenant id, or <c>null</c> if unresolved.</summary>
    Guid? TenantId { get; }

    /// <summary>Tenant slug (lowercase subdomain-safe identifier), or <c>null</c> if unresolved.</summary>
    string? Slug { get; }
}
