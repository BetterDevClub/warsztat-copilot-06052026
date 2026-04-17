using BookSlot.Domain.Abstractions;

namespace BookSlot.Api.Identity;

/// <summary>
/// Placeholder <see cref="ICurrentUser"/> used before authentication is wired (Phase 6).
/// Always reports an anonymous caller.
/// </summary>
internal sealed class AnonymousCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public bool IsAuthenticated => false;

    /// <inheritdoc />
    public Guid? UserId => null;

    /// <inheritdoc />
    public string? Email => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles { get; } = [];

    /// <inheritdoc />
    public bool IsInRole(string role) => false;
}

/// <summary>
/// Placeholder <see cref="ICurrentTenant"/> used before tenant resolution is wired (Phase 5).
/// Always reports an unresolved tenant.
/// </summary>
internal sealed class UnresolvedCurrentTenant : ICurrentTenant
{
    /// <inheritdoc />
    public bool IsResolved => false;

    /// <inheritdoc />
    public Guid? TenantId => null;

    /// <inheritdoc />
    public string? Slug => null;
}
