using BookSlot.Domain.Abstractions;

namespace BookSlot.MigrationRunner;

/// <summary>
/// Ambient tenant scope used by the standalone migration runner. Defaults to
/// unresolved (so EF tenant query filters return empty sets); the demo seeder
/// briefly enters the demo tenant scope when checking idempotency.
/// </summary>
internal sealed class AmbientCurrentTenant : ICurrentTenant
{
    private static readonly AsyncLocal<(Guid? Id, string? Slug)> Current = new();

    public bool IsResolved => Current.Value.Id is not null;
    public Guid? TenantId => Current.Value.Id;
    public string? Slug => Current.Value.Slug;

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

/// <summary>
/// Synthetic principal used by audit interceptors when the migration runner writes
/// rows during seeding (no HTTP user is present).
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId => null;
    public string? Email => "system@bookslot.migration-runner";
    public IReadOnlyCollection<string> Roles { get; } = new[] { "System" };
    public bool IsInRole(string role) => string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);
}
