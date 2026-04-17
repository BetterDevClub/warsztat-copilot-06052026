using BookSlot.Domain.Abstractions;

namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Scoped implementation of <see cref="ICurrentTenant"/>. The tenant resolution
/// middleware writes to this instance at the start of the request; every downstream
/// component (endpoint handlers, DbContext, validators) reads from the same scope.
/// </summary>
public sealed class CurrentTenantAccessor : ICurrentTenant
{
    private Guid? _tenantId;
    private string? _slug;

    /// <inheritdoc />
    public bool IsResolved => _tenantId is not null && !string.IsNullOrWhiteSpace(_slug);

    /// <inheritdoc />
    public Guid? TenantId => _tenantId;

    /// <inheritdoc />
    public string? Slug => _slug;

    /// <summary>Assign the tenant for the current scope. Called once by the middleware.</summary>
    public void Set(Guid tenantId, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        _tenantId = tenantId;
        _slug = slug.Trim().ToLowerInvariant();
    }

    /// <summary>Clear any tenant assigned to this scope.</summary>
    public void Clear()
    {
        _tenantId = null;
        _slug = null;
    }
}
