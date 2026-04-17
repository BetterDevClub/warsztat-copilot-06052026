using System.ComponentModel.DataAnnotations;

namespace BookSlot.Infrastructure.Security;

/// <summary>
/// Bound from the <c>Auth:Jwt</c> configuration section. The signing key must be at
/// least 32 bytes (256 bits) for HS256 — enforced by <see cref="MinLengthAttribute"/>.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth:Jwt";

    /// <summary>Token issuer (<c>iss</c> claim).</summary>
    [Required]
    public string Issuer { get; set; } = "BookSlot";

    /// <summary>Token audience (<c>aud</c> claim).</summary>
    [Required]
    public string Audience { get; set; } = "BookSlot";

    /// <summary>HS256 signing key.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh token lifetime.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>HMAC pepper applied to API key secret hashing.</summary>
    [Required, MinLength(32)]
    public string ApiKeyPepper { get; set; } = string.Empty;
}
