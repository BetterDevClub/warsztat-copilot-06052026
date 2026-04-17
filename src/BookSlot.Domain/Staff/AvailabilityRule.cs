using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Staff;

/// <summary>
/// A recurring weekly availability window for a staff member. Stored as
/// (DayOfWeek, StartTime, EndTime) in the tenant's local time zone.
/// Multiple rules per day allow split shifts (e.g. 09:00–12:00 and 14:00–18:00).
/// </summary>
public sealed class AvailabilityRule : Entity<Guid>, ITenantScoped
{
    private AvailabilityRule() { }

    private AvailabilityRule(Guid id, Guid tenantId, Guid staffId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Owning staff member.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>Day of the week the rule applies to.</summary>
    public DayOfWeek DayOfWeek { get; private set; }

    /// <summary>Window start in the tenant's local time.</summary>
    public TimeOnly StartTime { get; private set; }

    /// <summary>Window end in the tenant's local time (exclusive).</summary>
    public TimeOnly EndTime { get; private set; }

    /// <summary>Factory with validation.</summary>
    public static Result<AvailabilityRule> Create(Guid id, Guid tenantId, Guid staffId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
        {
            return Result.Failure<AvailabilityRule>(Error.Validation(
                "AvailabilityRule.InvalidWindow",
                "End time must be after start time."));
        }
        return new AvailabilityRule(id, tenantId, staffId, dayOfWeek, startTime, endTime);
    }
}
