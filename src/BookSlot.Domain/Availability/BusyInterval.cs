namespace BookSlot.Domain.Availability;

/// <summary>
/// A half-open time interval [Start, End) in UTC. Used for busy periods
/// (existing bookings + active reservations) passed to <see cref="AvailabilityEngine"/>.
/// </summary>
public readonly record struct BusyInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    /// <summary>True when this interval overlaps with another half-open interval.</summary>
    public bool Overlaps(DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => StartUtc < otherEnd && otherStart < EndUtc;
}
