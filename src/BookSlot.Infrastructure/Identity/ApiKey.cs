using BookSlot.Domain.Abstractions;

namespace BookSlot.Infrastructure.Identity;

/// <summary>
/// Machine-to-machine credential. The raw key (prefix + secret) is shown to the caller
/// exactly once at creation. Only <see cref="KeyHash"/> (HMAC-SHA256 of the secret
/// segment, keyed with the server pepper) is persisted; the prefix is indexed for O(1)
/// lookup on inbound requests.
/// </summary>
public class ApiKey : ITenantScoped
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning tenant id. Global query filter ensures cross-tenant isolation.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Human-readable label for the key (e.g. "CI pipeline").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Public key prefix, shown in listings (e.g. <c>bk_live_8f3a...</c>).</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 hash of the secret segment, hex-encoded.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>User id of the caller that created the key, for audit.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Set when the key has been revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Last observed use, for staleness reporting.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
