namespace BookSlot.Domain.Notifications;

/// <summary>Transport abstraction for SMS delivery.</summary>
public interface ISmsSender
{
    /// <summary>Sends an SMS. Returns the provider message id, or null.</summary>
    Task<string?> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Outbound SMS payload.</summary>
/// <param name="To">Recipient phone number in E.164 format.</param>
/// <param name="Body">Plain-text message body.</param>
public sealed record SmsMessage(string To, string Body);
