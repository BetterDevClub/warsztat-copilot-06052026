using Microsoft.AspNetCore.Identity;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Tenant-scoped identity user. <see cref="TenantId"/> is set at registration time
/// (Phase 7) and never mutated. Email uniqueness is enforced per tenant via a
/// composite unique index configured in <see cref="Persistence.AppDbContext"/>.
/// </summary>
/// <remarks>
/// Not marked with <see cref="Domain.Abstractions.ITenantScoped"/> on purpose:
/// public auth flows (password reset, email confirmation) need to resolve users
/// before a tenant is bound to the scope. Slices filter by tenant explicitly.
/// </remarks>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The owning tenant id.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Creation timestamp (UTC), stamped by the infrastructure.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
