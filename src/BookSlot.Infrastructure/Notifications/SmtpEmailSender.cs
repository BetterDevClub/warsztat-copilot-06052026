using System.Net;
using System.Net.Mail;
using BookSlot.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>SMTP-backed implementation of <see cref="IEmailSender"/> using
/// <see cref="System.Net.Mail.SmtpClient"/>. Suitable for dev (MailHog) and basic
/// production relays; swap for MailKit / SendGrid for advanced scenarios.</summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    /// <summary>Creates a new instance from bound options.</summary>
    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.TextBody, null, "text/plain"));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            message.HtmlBody, null, "text/html"));

        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
            client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);

        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
        // System.Net.Mail does not expose a provider message id.
        return null;
    }
}
