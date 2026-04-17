using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Webhooks;

/// <summary>
/// A subscriber-configured HTTP endpoint that receives webhook deliveries when
/// events from <see cref="WebhookEventTypes"/> occur in the owning tenant.
/// </summary>
public sealed class WebhookEndpoint : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Maximum URL length.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>Maximum description length.</summary>
    public const int MaxDescriptionLength = 300;

    /// <summary>Maximum secret length (bytes of base64 or hex).</summary>
    public const int MaxSecretLength = 256;

    private readonly List<string> _subscribedEvents = new();

    private WebhookEndpoint() { }

    private WebhookEndpoint(
        Guid id,
        Guid tenantId,
        string url,
        string secret,
        IEnumerable<string> subscribedEvents,
        string? description,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        Url = url;
        Secret = secret;
        Description = description;
        IsActive = true;
        CreatedAt = createdAt;
        _subscribedEvents.AddRange(subscribedEvents);
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Target HTTP(S) URL.</summary>
    public string Url { get; private set; } = default!;

    /// <summary>HMAC signing secret — send as <c>X-BookSlot-Signature</c> header.</summary>
    public string Secret { get; private set; } = default!;

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether the endpoint currently receives deliveries.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Event types this endpoint subscribes to.</summary>
    public IReadOnlyList<string> SubscribedEvents => _subscribedEvents;

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of last update.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    // -------------------------------------------------------------------------

    /// <summary>Returns true if this endpoint subscribes to the given event type.</summary>
    public bool SubscribesTo(string eventType) => _subscribedEvents.Contains(eventType);

    /// <summary>Updates mutable fields of the endpoint.</summary>
    public Result Update(
        string url,
        IEnumerable<string> subscribedEvents,
        string? description,
        bool isActive,
        DateTimeOffset now)
    {
        var validation = Validate(url, subscribedEvents, description, out var normalisedEvents);
        if (validation.IsFailure) return validation;

        Url = url.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        _subscribedEvents.Clear();
        _subscribedEvents.AddRange(normalisedEvents);
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Rotates the signing secret.</summary>
    public void RotateSecret(string newSecret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(newSecret) || newSecret.Length > MaxSecretLength)
            throw new ArgumentException($"Secret must be non-empty and at most {MaxSecretLength} chars.", nameof(newSecret));
        Secret = newSecret;
        UpdatedAt = now;
    }

    /// <summary>Disables the endpoint without deleting history.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    // -------------------------------------------------------------------------

    /// <summary>Creates a new, active endpoint.</summary>
    public static Result<WebhookEndpoint> Create(
        Guid id,
        Guid tenantId,
        string url,
        string secret,
        IEnumerable<string> subscribedEvents,
        string? description,
        DateTimeOffset now)
    {
        var validation = Validate(url, subscribedEvents, description, out var normalisedEvents);
        if (validation.IsFailure)
            return Result.Failure<WebhookEndpoint>(validation.Error);

        if (string.IsNullOrWhiteSpace(secret) || secret.Length > MaxSecretLength)
            return Result.Failure<WebhookEndpoint>(Primitives.Error.Validation("WebhookEndpoint.SecretInvalid",
                $"Secret is required and must be {MaxSecretLength} characters or fewer."));

        return new WebhookEndpoint(id, tenantId, url.Trim(), secret,
            normalisedEvents, description?.Trim(), now);
    }

    private static Result Validate(string url, IEnumerable<string> subscribedEvents, string? description,
        out List<string> normalisedEvents)
    {
        normalisedEvents = new List<string>();

        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength)
            return Result.Failure(Primitives.Error.Validation("WebhookEndpoint.UrlInvalid",
                $"URL is required and must be {MaxUrlLength} characters or fewer."));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            return Result.Failure(Primitives.Error.Validation("WebhookEndpoint.UrlNotHttp",
                "URL must be an absolute http:// or https:// URL."));

        if (description is not null && description.Length > MaxDescriptionLength)
            return Result.Failure(Primitives.Error.Validation("WebhookEndpoint.DescriptionTooLong",
                $"Description must be {MaxDescriptionLength} characters or fewer."));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evt in subscribedEvents ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(evt)) continue;
            var trimmed = evt.Trim();
            if (!WebhookEventTypes.All.Contains(trimmed))
                return Result.Failure(Primitives.Error.Validation("WebhookEndpoint.UnknownEvent",
                    $"Unknown event type '{trimmed}'."));
            if (seen.Add(trimmed)) normalisedEvents.Add(trimmed);
        }

        if (normalisedEvents.Count == 0)
            return Result.Failure(Primitives.Error.Validation("WebhookEndpoint.NoEvents",
                "At least one subscribed event is required."));

        return Result.Success();
    }
}
