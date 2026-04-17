namespace BookSlot.Features.Shared.Emailing;

/// <summary>
/// Outbound transactional email abstraction. Real SMTP/SendGrid adapters land in
/// Phase 15 — Phase 6 ships with <see cref="NoOpEmailSender"/> which logs message
/// previews for local development.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends an email. Implementations must honour <paramref name="cancellationToken"/>.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Minimal email payload consumed by <see cref="IEmailSender"/>.</summary>
/// <param name="To">Recipient email address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body. Implementations may derive a plain-text fallback.</param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody);
