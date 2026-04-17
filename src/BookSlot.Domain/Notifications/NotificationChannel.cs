namespace BookSlot.Domain.Notifications;

/// <summary>Delivery medium for a notification.</summary>
public enum NotificationChannel
{
    /// <summary>Transactional email.</summary>
    Email = 0,

    /// <summary>SMS / text message.</summary>
    Sms = 1,
}
