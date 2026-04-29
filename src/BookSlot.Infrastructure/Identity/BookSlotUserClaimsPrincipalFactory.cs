using System.Security.Claims;
using BookSlot.Infrastructure.Persistence;
using BookSlot.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Augments the default claims principal with BookSlot-specific claims:
/// <see cref="JwtTokenGenerator.TenantSlugClaim"/> (<c>tenant_slug</c>) and
/// <c>tenant_id</c>. Stamping these into the cookie means the
/// <c>TenantResolutionMiddleware</c> (and <c>TenantCircuitHandler</c> in the Blazor
/// Web host) can resolve the ambient tenant from any authentication context —
/// SSR HTTP requests, Interactive Server circuits, and API JWT tokens alike.
/// </summary>
public sealed class BookSlotUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly AppDbContext _db;

    /// <summary>Creates a new factory.</summary>
    public BookSlotUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options,
        AppDbContext db)
        : base(userManager, roleManager, options)
    {
        _db = db;
    }

    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);

        // Tenants are NOT ITenantScoped — no query filter, safe to read at login time
        // even though _currentTenant is unresolved in this scope.
        var slug = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.Slug)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(slug))
        {
            identity.AddClaim(new Claim(JwtTokenGenerator.TenantSlugClaim, slug));
            identity.AddClaim(new Claim("tenant_id", user.TenantId.ToString()));
        }

        return identity;
    }
}
