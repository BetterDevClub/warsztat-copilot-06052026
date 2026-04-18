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

    /// <summary>Per-tenant summary of the next day's bookings, sent at local 18:00.</summary>
    DailyDigest = 30,

    /// <summary>Per-tenant archive of the previous month's activity, sent early on day 1.</summary>
    MonthlyReport = 31,
}
