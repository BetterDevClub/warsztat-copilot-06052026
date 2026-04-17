using BookSlot.Domain.Abstractions;

namespace BookSlot.Worker.Composition;

/// <summary>
/// Worker-side <see cref="ICurrentUser"/> stub. Audit columns attribute all
/// worker-originated writes to the synthetic "system" principal so downstream
/// reports can distinguish user actions from background automation.
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public string? Email => "system@bookslot.worker";
    public IReadOnlyCollection<string> Roles { get; } = new[] { "System" };
    public bool IsInRole(string role) => string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Ambient tenant scope for worker jobs. Defaults to unresolved; jobs that
/// operate on tenant-scoped data set the current tenant for the duration of
/// their unit of work via <see cref="EnterScope"/>.
/// </summary>
internal sealed class AmbientCurrentTenant : ICurrentTenant
{
    private static readonly AsyncLocal<(Guid? Id, string? Slug)> Current = new();

    public bool IsResolved => Current.Value.Id is not null;
    public Guid? TenantId => Current.Value.Id;
    public string? Slug => Current.Value.Slug;

    /// <summary>
    /// Push a tenant onto the ambient async-local stack. Dispose the returned
    /// scope to restore the previous value.
    /// </summary>
    public static IDisposable EnterScope(Guid tenantId, string slug)
    {
        var previous = Current.Value;
        Current.Value = (tenantId, slug);
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly (Guid? Id, string? Slug) _previous;
        public Scope((Guid? Id, string? Slug) previous) => _previous = previous;
        public void Dispose() => Current.Value = _previous;
    }
}
