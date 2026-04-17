namespace BookSlot.Domain.Notifications;

/// <summary>Business-level purpose of a notification. Drives template selection.</summary>
public enum NotificationKind
{
    /// <summary>Booking confirmed (email sent after successful create).</summary>
    BookingConfirmed = 0,

    /// <summary>Booking cancelled.</summary>
    BookingCancelled = 1,

    /// <summary>Booking rescheduled to a new slot.</summary>
    BookingRescheduled = 2,

    /// <summary>Reminder sent 24 hours before the appointment.</summary>
    ReminderT24h = 10,

    /// <summary>Reminder sent 2 hours before the appointment.</summary>
    ReminderT2h = 11,

    /// <summary>Password reset link for a tenant user.</summary>
    PasswordReset = 20,

    /// <summary>Welcome email to a newly provisioned staff account.</summary>
    StaffWelcome = 21,
}
