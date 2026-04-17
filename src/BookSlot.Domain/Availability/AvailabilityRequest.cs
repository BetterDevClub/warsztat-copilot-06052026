using BookSlot.Domain.Primitives;
using BookSlot.Domain.Staff;

namespace BookSlot.Domain.Availability;

/// <summary>
/// Input to <see cref="AvailabilityEngine.GenerateSlots"/>. Rules/overrides are interpreted in
/// <see cref="TimeZone"/>; the engine emits UTC slots.
/// </summary>
public sealed class AvailabilityRequest
{
    /// <summary>Tenant-local time zone.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>Inclusive start of the search window in UTC.</summary>
    public required DateTimeOffset FromUtc { get; init; }

    /// <summary>Exclusive end of the search window in UTC.</summary>
    public required DateTimeOffset ToUtc { get; init; }

    /// <summary>Length of the appointment in minutes (core body, excluding buffers).</summary>
    public required int DurationMinutes { get; init; }

    /// <summary>Non-bookable buffer before the appointment.</summary>
    public int BufferBeforeMinutes { get; init; }

    /// <summary>Non-bookable buffer after the appointment.</summary>
    public int BufferAfterMinutes { get; init; }

    /// <summary>Granularity of candidate start times in minutes (e.g. 15, 30).</summary>
    public int SlotIntervalMinutes { get; init; } = 15;

    /// <summary>Maximum parallel bookings at the same moment. Defaults to 1.</summary>
    public int MaxConcurrent { get; init; } = 1;

    /// <summary>Weekly rules for the staff member.</summary>
    public required IReadOnlyCollection<AvailabilityRule> Rules { get; init; }

    /// <summary>Date-specific overrides (unavailability + extra windows).</summary>
    public required IReadOnlyCollection<AvailabilityOverride> Overrides { get; init; }

    /// <summary>Already-booked or reserved UTC intervals.</summary>
    public IReadOnlyCollection<BusyInterval> Busy { get; init; } = Array.Empty<BusyInterval>();

    internal Result Validate()
    {
        if (DurationMinutes <= 0)
            return Result.Failure(Error.Validation("Availability.InvalidDuration", "Duration must be positive."));
        if (SlotIntervalMinutes <= 0)
            return Result.Failure(Error.Validation("Availability.InvalidInterval", "Slot interval must be positive."));
        if (BufferBeforeMinutes < 0 || BufferAfterMinutes < 0)
            return Result.Failure(Error.Validation("Availability.NegativeBuffer", "Buffers cannot be negative."));
        if (MaxConcurrent <= 0)
            return Result.Failure(Error.Validation("Availability.InvalidConcurrency", "MaxConcurrent must be positive."));
        if (ToUtc <= FromUtc)
            return Result.Failure(Error.Validation("Availability.InvalidRange", "ToUtc must be after FromUtc."));
        return Result.Success();
    }
}
