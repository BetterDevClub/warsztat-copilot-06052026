namespace BookSlot.Domain.Availability;

/// <summary>
/// A single bookable slot returned by <see cref="AvailabilityEngine"/>.
/// Times are in UTC.
/// </summary>
/// <param name="StartUtc">Inclusive slot start in UTC.</param>
/// <param name="EndUtc">Exclusive slot end in UTC (StartUtc + service duration).</param>
public readonly record struct AvailabilitySlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
