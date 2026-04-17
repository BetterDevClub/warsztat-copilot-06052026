namespace BookSlot.Domain.Bookings;

/// <summary>Lifecycle states of a <see cref="RecurringBooking"/>.</summary>
public enum RecurringBookingStatus
{
    /// <summary>Active — the generator will keep producing instances.</summary>
    Active = 0,

    /// <summary>Cancelled — no further instances will be generated.</summary>
    Cancelled = 1,
}
