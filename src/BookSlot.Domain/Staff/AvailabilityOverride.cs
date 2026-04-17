using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Staff;

/// <summary>
/// One-off override of the weekly availability: either extra hours on a specific date
/// or full-day unavailability (holiday). Takes precedence over <see cref="AvailabilityRule"/>.
/// </summary>
public sealed class AvailabilityOverride : Entity<Guid>, ITenantScoped
{
    /// <summary>Maximum length of the optional reason field.</summary>
    public const int MaxReasonLength = 200;

    private AvailabilityOverride() { }

    private AvailabilityOverride(Guid id, Guid tenantId, Guid staffId, DateOnly date, bool isUnavailable, TimeOnly? startTime, TimeOnly? endTime, string? reason) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        Date = date;
        IsUnavailable = isUnavailable;
        StartTime = startTime;
        EndTime = endTime;
        Reason = reason;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning staff member.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>The date (tenant local) this override applies to.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>If true, the staff is fully unavailable on <see cref="Date"/>.</summary>
    public bool IsUnavailable { get; private set; }

    /// <summary>Window start when <see cref="IsUnavailable"/> is false.</summary>
    public TimeOnly? StartTime { get; private set; }

    /// <summary>Window end when <see cref="IsUnavailable"/> is false.</summary>
    public TimeOnly? EndTime { get; private set; }

    /// <summary>Optional human-readable reason (e.g. "Public holiday", "Training day").</summary>
    public string? Reason { get; private set; }

    /// <summary>Creates an "unavailable whole day" override.</summary>
    public static Result<AvailabilityOverride> Unavailable(Guid id, Guid tenantId, Guid staffId, DateOnly date, string? reason)
    {
        var reasonResult = ValidateReason(reason);
        if (reasonResult.IsFailure) return Result.Failure<AvailabilityOverride>(reasonResult.Error);

        return new AvailabilityOverride(id, tenantId, staffId, date, isUnavailable: true, startTime: null, endTime: null, reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    /// <summary>Creates an "extra hours on this date" override.</summary>
    public static Result<AvailabilityOverride> Window(Guid id, Guid tenantId, Guid staffId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string? reason)
    {
        if (endTime <= startTime)
        {
            return Result.Failure<AvailabilityOverride>(Error.Validation(
                "AvailabilityOverride.InvalidWindow",
                "End time must be after start time."));
        }
        var reasonResult = ValidateReason(reason);
        if (reasonResult.IsFailure) return Result.Failure<AvailabilityOverride>(reasonResult.Error);

        return new AvailabilityOverride(id, tenantId, staffId, date, isUnavailable: false, startTime: startTime, endTime: endTime, reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }

    private static Result ValidateReason(string? reason)
    {
        if (reason is not null && reason.Length > MaxReasonLength)
        {
            return Result.Failure(Error.Validation(
                "AvailabilityOverride.ReasonTooLong",
                $"Reason must be {MaxReasonLength} characters or fewer."));
        }
        return Result.Success();
    }
}
