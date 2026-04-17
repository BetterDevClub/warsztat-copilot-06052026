namespace BookSlot.Domain.Notifications;

/// <summary>Transport abstraction for transactional emails.</summary>
public interface IEmailSender
{
    /// <summary>Sends an email. The implementation should be idempotent-friendly:
    /// callers are expected to have already persisted a <see cref="NotificationLog"/>
    /// with a dedup key.</summary>
    /// <returns>A provider-assigned message id, or null if none.</returns>
    Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Outbound email payload.</summary>
/// <param name="To">Recipient email address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body (preferred rendering).</param>
/// <param name="TextBody">Plain-text fallback body.</param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
