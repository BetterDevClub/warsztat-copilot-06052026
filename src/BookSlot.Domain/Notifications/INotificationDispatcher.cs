namespace BookSlot.Domain.Notifications;

/// <summary>
/// Coordinates notification rendering, idempotent logging and transport delivery.
/// Implementations persist a <see cref="NotificationLog"/> keyed on
/// <c>(TenantId, DedupKey)</c> before attempting delivery and mark the entry
/// Sent/Failed/Suppressed afterwards.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>Dispatches a notification. Returns the created (or matched) log entry.</summary>
    Task<NotificationDispatchResult> DispatchAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Input to <see cref="INotificationDispatcher"/>.</summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="Kind">Business reason.</param>
/// <param name="Channel">Delivery medium.</param>
/// <param name="Recipient">Email address or E.164 phone.</param>
/// <param name="DedupKey">
/// Stable key enforcing idempotency per tenant. Example: <c>booking:{id}:confirmed</c>.
/// If a log entry with the same key already exists and is Sent or Suppressed the call is a no-op.
/// </param>
/// <param name="TemplateContext">Arbitrary data passed to the template renderer.</param>
public sealed record NotificationRequest(
    Guid TenantId,
    NotificationKind Kind,
    NotificationChannel Channel,
    string Recipient,
    string DedupKey,
    IReadOnlyDictionary<string, object?>? TemplateContext = null);

/// <summary>Outcome of a dispatch attempt.</summary>
/// <param name="LogId">Primary key of the <see cref="NotificationLog"/> entry.</param>
/// <param name="Status">Final status after the dispatch attempt.</param>
/// <param name="Duplicate">True when a prior log entry matched the dedup key and no new send was performed.</param>
public sealed record NotificationDispatchResult(Guid LogId, NotificationStatus Status, bool Duplicate);
