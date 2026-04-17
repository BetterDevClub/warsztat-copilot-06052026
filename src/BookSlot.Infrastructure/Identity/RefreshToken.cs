namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Refresh token row. Only the SHA-256 hash of the opaque token is persisted; the raw
/// token is returned once at issuance. Rotation is mandatory on every refresh — the old
/// row is marked revoked and <see cref="ReplacedByTokenHash"/> points at its successor.
/// </summary>
public class RefreshToken
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant that owns the user this token belongs to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Tenant slug captured at issuance — needed for the new access token's claim.</summary>
    public string TenantSlug { get; set; } = string.Empty;

    /// <summary>User the token authenticates.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash (hex-encoded) of the raw refresh token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Absolute expiration timestamp.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token has been revoked (rotation, logout, or theft response).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>If rotated, the hash of the replacement token.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>True when the token is still usable.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
