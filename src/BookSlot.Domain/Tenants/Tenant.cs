using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.Domain.Tenants;

/// <summary>
/// The tenant is the top-level isolation unit of the SaaS. Every aggregate below
/// references a <see cref="Tenant"/> via <c>TenantId</c> (see <see cref="Abstractions.ITenantScoped"/>).
/// Tenants themselves are NOT tenant-scoped — they are looked up globally by slug or id.
/// </summary>
public sealed class Tenant : AggregateRoot<Guid>
{
    private Tenant() { }

    private Tenant(Guid id, string slug, string name, DateTimeOffset createdAt) : base(id)
    {
        Slug = slug;
        Name = name;
        CreatedAt = createdAt;
        IsActive = true;
    }

    /// <summary>Immutable, subdomain-safe identifier. Unique across the platform.</summary>
    public string Slug { get; private set; } = default!;

    /// <summary>Human-readable display name. May be changed via <see cref="Rename"/>.</summary>
    public string Name { get; private set; } = default!;

    /// <summary>Creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether the tenant is currently active. Soft-delete flag.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates a new tenant aggregate. Caller is responsible for uniqueness checks
    /// against the database before persisting.
    /// </summary>
    public static Result<Tenant> Create(Guid id, TenantSlug slug, string name, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(slug);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(Error.Validation("Tenant.NameRequired", "Tenant name is required."));
        }
        if (name.Length > 200)
        {
            return Result.Failure<Tenant>(Error.Validation("Tenant.NameTooLong", "Tenant name must be 200 characters or fewer."));
        }

        return new Tenant(id, slug.Value, name.Trim(), createdAt);
    }

    /// <summary>Renames the tenant (display name only — slug is immutable).</summary>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return Result.Failure(Error.Validation("Tenant.NameInvalid", "Tenant name must be 1–200 characters."));
        }
        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Deactivates the tenant. Idempotent.</summary>
    public void Deactivate() => IsActive = false;
}
