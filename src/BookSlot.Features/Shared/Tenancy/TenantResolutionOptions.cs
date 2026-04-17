namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Configuration for <see cref="TenantResolutionMiddleware"/>. Bound to the
/// <c>Tenancy</c> section of configuration.
/// </summary>
public sealed class TenantResolutionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Request header carrying the tenant slug in development. Checked after the JWT claim
    /// and subdomain. Default: <c>X-Tenant-Slug</c>.
    /// </summary>
    public string HeaderName { get; set; } = "X-Tenant-Slug";

    /// <summary>
    /// JWT claim type that carries the tenant slug (Phase 6). Default: <c>tenant_slug</c>.
    /// </summary>
    public string SlugClaimType { get; set; } = "tenant_slug";

    /// <summary>
    /// Root domains under which the first DNS label is treated as the tenant subdomain
    /// (e.g. <c>bookslot.app</c> → <c>acme.bookslot.app</c>). Hosts not matching any root
    /// fall back to the header.
    /// </summary>
    public IList<string> RootDomains { get; } = [];

    /// <summary>
    /// Subdomains that are NOT tenants (www, api, admin, localhost, ...). Case-insensitive.
    /// </summary>
    public IList<string> ReservedSubdomains { get; } = new List<string>
    {
        "www",
        "api",
        "admin",
        "localhost",
    };
}
