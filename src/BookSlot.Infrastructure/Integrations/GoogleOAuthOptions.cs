namespace BookSlot.Infrastructure.Integrations;

/// <summary>
/// Configuration for Google Calendar OAuth2. Bound from the <c>Integrations:Google</c>
/// section. Phase 18 uses these values only to build the consent URL and persist the
/// callback code; full token exchange + calendar sync wiring lands in Phase 22.
/// </summary>
public sealed class GoogleOAuthOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Integrations:Google";

    /// <summary>OAuth2 client id obtained from Google Cloud Console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret. Never log this value.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Absolute callback URL registered with Google; must match exactly.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>OAuth scopes requested from the user — space-separated.</summary>
    public string Scopes { get; set; } = "https://www.googleapis.com/auth/calendar.events openid email";
}
