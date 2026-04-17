namespace BookSlot.Domain.Notifications;

/// <summary>Renders per-kind notification content from a template context.</summary>
public interface INotificationTemplateRenderer
{
    /// <summary>Renders an email. Returns null when the kind has no email template.</summary>
    EmailContent? RenderEmail(NotificationKind kind, IReadOnlyDictionary<string, object?>? context);

    /// <summary>Renders an SMS body. Returns null when the kind has no SMS template.</summary>
    string? RenderSms(NotificationKind kind, IReadOnlyDictionary<string, object?>? context);
}

/// <summary>Rendered email payload (minus recipient).</summary>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body.</param>
/// <param name="TextBody">Plain-text fallback body.</param>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);
