namespace BookSlot.Domain.Bookings;

/// <summary>Lifecycle states of a <see cref="Booking"/>.</summary>
public enum BookingStatus
{
    /// <summary>Created but not yet confirmed (e.g. awaiting payment).</summary>
    Pending = 0,

    /// <summary>Confirmed and active.</summary>
    Confirmed = 1,

    /// <summary>Cancelled by the guest or an admin.</summary>
    Cancelled = 2,

    /// <summary>Guest did not show up and was marked accordingly.</summary>
    NoShow = 3,

    /// <summary>The booking was superseded by a rescheduled booking.</summary>
    Rescheduled = 4,
}
