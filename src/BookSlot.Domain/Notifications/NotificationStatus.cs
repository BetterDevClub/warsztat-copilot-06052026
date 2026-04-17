namespace BookSlot.Domain.Notifications;

/// <summary>Delivery state of a notification log entry.</summary>
public enum NotificationStatus
{
    /// <summary>Accepted for dispatch; no attempt made yet.</summary>
    Pending = 0,

    /// <summary>Provider accepted the message for delivery.</summary>
    Sent = 1,

    /// <summary>Provider rejected the message or a transport error occurred.</summary>
    Failed = 2,

    /// <summary>Silently suppressed (e.g. tenant opted out of the channel).</summary>
    Suppressed = 3,
}
