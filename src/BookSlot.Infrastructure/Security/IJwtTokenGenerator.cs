using BookSlot.Infrastructure.Identity;

namespace BookSlot.Infrastructure.Security;

/// <summary>Issues signed JWT access tokens for authenticated users.</summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Creates a signed access token for <paramref name="user"/>, embedding the
    /// tenant slug, role claims, and standard JWT registered claims.
    /// </summary>
    AccessToken CreateAccessToken(ApplicationUser user, string tenantSlug, IEnumerable<string> roles);
}

/// <summary>Issued access token with its expiry (UTC).</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
