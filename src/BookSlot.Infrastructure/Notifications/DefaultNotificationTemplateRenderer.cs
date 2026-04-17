using System.Globalization;
using BookSlot.Domain.Notifications;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>
/// Inline-template renderer covering the core set of kinds needed before
/// Phase 28 ships Razor-based templates. Values from the context dictionary are
/// interpolated via simple <c>{Name}</c> placeholders.
/// </summary>
internal sealed class DefaultNotificationTemplateRenderer : INotificationTemplateRenderer
{
    /// <inheritdoc />
    public EmailContent? RenderEmail(NotificationKind kind, IReadOnlyDictionary<string, object?>? context)
    {
        var ctx = context ?? EmptyContext;
        return kind switch
        {
            NotificationKind.BookingConfirmed => Render(
                "Rezerwacja potwierdzona — {ServiceName}",
                "<p>Cześć {GuestName},</p><p>Twoja rezerwacja <strong>{ServiceName}</strong> na {StartLocal} została potwierdzona.</p>",
                "Cześć {GuestName},\n\nTwoja rezerwacja {ServiceName} na {StartLocal} została potwierdzona.",
                ctx),
            NotificationKind.BookingCancelled => Render(
                "Rezerwacja anulowana — {ServiceName}",
                "<p>Cześć {GuestName},</p><p>Twoja rezerwacja <strong>{ServiceName}</strong> na {StartLocal} została anulowana.</p>",
                "Cześć {GuestName},\n\nTwoja rezerwacja {ServiceName} na {StartLocal} została anulowana.",
                ctx),
            NotificationKind.BookingRescheduled => Render(
                "Rezerwacja przeniesiona — {ServiceName}",
                "<p>Cześć {GuestName},</p><p>Twoja rezerwacja <strong>{ServiceName}</strong> została przeniesiona na {StartLocal}.</p>",
                "Cześć {GuestName},\n\nTwoja rezerwacja {ServiceName} została przeniesiona na {StartLocal}.",
                ctx),
            NotificationKind.ReminderT24h => Render(
                "Przypomnienie — wizyta jutro o {StartLocal}",
                "<p>Cześć {GuestName},</p><p>Przypominamy o wizycie <strong>{ServiceName}</strong> jutro o {StartLocal}.</p>",
                "Cześć {GuestName},\n\nPrzypominamy o wizycie {ServiceName} jutro o {StartLocal}.",
                ctx),
            NotificationKind.ReminderT2h => Render(
                "Przypomnienie — wizyta za 2 godziny",
                "<p>Cześć {GuestName},</p><p>Za 2 godziny masz wizytę <strong>{ServiceName}</strong> ({StartLocal}).</p>",
                "Cześć {GuestName},\n\nZa 2 godziny masz wizytę {ServiceName} ({StartLocal}).",
                ctx),
            NotificationKind.PasswordReset => Render(
                "Reset hasła w BookSlot",
                "<p>Kliknij ten link aby zresetować hasło: <a href=\"{ResetUrl}\">{ResetUrl}</a>. Link wygasa za 1 godzinę.</p>",
                "Link do resetu hasła: {ResetUrl}\nLink wygasa za 1 godzinę.",
                ctx),
            NotificationKind.StaffWelcome => Render(
                "Zaproszenie do BookSlot",
                "<p>Cześć {DisplayName},</p><p>Twoje konto pracownika zostało utworzone. Zaloguj się tutaj: <a href=\"{LoginUrl}\">{LoginUrl}</a>.</p>",
                "Cześć {DisplayName},\n\nTwoje konto pracownika zostało utworzone. Zaloguj się: {LoginUrl}",
                ctx),
            _ => null,
        };
    }

    /// <inheritdoc />
    public string? RenderSms(NotificationKind kind, IReadOnlyDictionary<string, object?>? context)
    {
        var ctx = context ?? EmptyContext;
        return kind switch
        {
            NotificationKind.BookingConfirmed => Interpolate("BookSlot: {ServiceName} potwierdzone na {StartLocal}.", ctx),
            NotificationKind.BookingCancelled => Interpolate("BookSlot: {ServiceName} ({StartLocal}) anulowane.", ctx),
            NotificationKind.BookingRescheduled => Interpolate("BookSlot: {ServiceName} przeniesione na {StartLocal}.", ctx),
            NotificationKind.ReminderT24h => Interpolate("BookSlot: przypomnienie — {ServiceName} jutro o {StartLocal}.", ctx),
            NotificationKind.ReminderT2h => Interpolate("BookSlot: za 2h wizyta {ServiceName} o {StartLocal}.", ctx),
            _ => null,
        };
    }

    private static EmailContent Render(string subject, string html, string text, IReadOnlyDictionary<string, object?> ctx)
        => new(Interpolate(subject, ctx), Interpolate(html, ctx), Interpolate(text, ctx));

    private static string Interpolate(string template, IReadOnlyDictionary<string, object?> ctx)
    {
        if (string.IsNullOrEmpty(template) || ctx.Count == 0) return template;
        var result = template;
        foreach (var (key, value) in ctx)
        {
            var stringified = value switch
            {
                null => string.Empty,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
            result = result.Replace("{" + key + "}", stringified, StringComparison.Ordinal);
        }
        return result;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyContext
        = new Dictionary<string, object?>();
}
