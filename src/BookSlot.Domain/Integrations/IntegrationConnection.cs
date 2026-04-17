using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Integrations;

/// <summary>
/// Persisted OAuth2 credentials for a third-party integration (Google, Zoom, …)
/// tied to a tenant and optionally a specific staff member.
/// </summary>
public sealed class IntegrationConnection : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Maximum token length stored (encryption-at-rest deferred to Phase 32).</summary>
    public const int MaxTokenLength = 4096;

    /// <summary>Maximum scope string length.</summary>
    public const int MaxScopeLength = 1024;

    /// <summary>Maximum external account identifier length.</summary>
    public const int MaxAccountLength = 320;

    private IntegrationConnection() { }

    private IntegrationConnection(
        Guid id,
        Guid tenantId,
        MeetingProvider provider,
        Guid? staffId,
        string externalAccountId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? accessTokenExpiresAt,
        string? scope,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        Provider = provider;
        StaffId = staffId;
        ExternalAccountId = externalAccountId;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        Scope = scope;
        IsActive = true;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Which provider this connection authenticates to.</summary>
    public MeetingProvider Provider { get; private set; }

    /// <summary>Optional staff member this connection belongs to (null = tenant-wide).</summary>
    public Guid? StaffId { get; private set; }

    /// <summary>External account identifier (Google e-mail, Zoom user id, …).</summary>
    public string ExternalAccountId { get; private set; } = default!;

    /// <summary>Current OAuth2 access token, null while waiting for the first refresh.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>Long-lived refresh token — required to keep the connection alive.</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>When the current access token expires.</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; private set; }

    /// <summary>Granted OAuth scope string (space separated).</summary>
    public string? Scope { get; private set; }

    /// <summary>Whether this connection is currently usable.</summary>
    public bool IsActive { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of the last token refresh / revoke.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Creates a new active connection (typically from an OAuth callback).</summary>
    public static Result<IntegrationConnection> Create(
        Guid id,
        Guid tenantId,
        MeetingProvider provider,
        Guid? staffId,
        string externalAccountId,
        string? accessToken,
        string? refreshToken,
        DateTimeOffset? accessTokenExpiresAt,
        string? scope,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(externalAccountId) || externalAccountId.Length > MaxAccountLength)
            return Result.Failure<IntegrationConnection>(Error.Validation("IntegrationConnection.AccountInvalid",
                $"ExternalAccountId is required and must be {MaxAccountLength} characters or fewer."));

        if (accessToken is not null && accessToken.Length > MaxTokenLength)
            return Result.Failure<IntegrationConnection>(Error.Validation("IntegrationConnection.AccessTokenTooLong",
                $"AccessToken must be {MaxTokenLength} characters or fewer."));

        if (refreshToken is not null && refreshToken.Length > MaxTokenLength)
            return Result.Failure<IntegrationConnection>(Error.Validation("IntegrationConnection.RefreshTokenTooLong",
                $"RefreshToken must be {MaxTokenLength} characters or fewer."));

        if (scope is not null && scope.Length > MaxScopeLength)
            return Result.Failure<IntegrationConnection>(Error.Validation("IntegrationConnection.ScopeTooLong",
                $"Scope must be {MaxScopeLength} characters or fewer."));

        return new IntegrationConnection(id, tenantId, provider, staffId,
            externalAccountId.Trim(), accessToken, refreshToken, accessTokenExpiresAt, scope, now);
    }

    /// <summary>Replaces the stored tokens after a successful refresh.</summary>
    public void UpdateTokens(string accessToken, string? refreshToken, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > MaxTokenLength)
            throw new ArgumentException($"AccessToken must be non-empty and at most {MaxTokenLength} chars.", nameof(accessToken));

        AccessToken = accessToken;
        if (!string.IsNullOrWhiteSpace(refreshToken))
            RefreshToken = refreshToken;
        AccessTokenExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    /// <summary>Deactivates the connection (user revoked / provider returned invalid_grant).</summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        AccessToken = null;
        UpdatedAt = now;
    }
}
