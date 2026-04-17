namespace BookSlot.Domain.Webhooks;

/// <summary>
/// String constants for the webhook event types delivered to subscribers.
/// Stored on <see cref="WebhookEndpoint.SubscribedEvents"/> and emitted as the
/// <c>event</c> field in every delivery payload. Values are stable — never renamed
/// after shipping since external subscribers depend on them.
/// </summary>
public static class WebhookEventTypes
{
    /// <summary>Booking created (guest or admin).</summary>
    public const string BookingCreated = "booking.created";

    /// <summary>Booking cancelled.</summary>
    public const string BookingCancelled = "booking.cancelled";

    /// <summary>Booking rescheduled to a new slot.</summary>
    public const string BookingRescheduled = "booking.rescheduled";

    /// <summary>Booking marked as a no-show.</summary>
    public const string BookingNoShow = "booking.no_show";

    /// <summary>Recurring booking template cancelled.</summary>
    public const string RecurringBookingCancelled = "recurring_booking.cancelled";

    /// <summary>All recognised event types. Used for validating
    /// <see cref="WebhookEndpoint.SubscribedEvents"/> inputs.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BookingCreated,
        BookingCancelled,
        BookingRescheduled,
        BookingNoShow,
        RecurringBookingCancelled,
    };
}
