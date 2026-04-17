using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Bookings;

/// <summary>
/// Template describing a recurring series of bookings. An instance-generator worker job
/// (Phase 23) materialises individual <see cref="Booking"/> rows from this template
/// on a rolling horizon. Guest/service/staff data is copied onto each materialised
/// booking so a later template edit does not retroactively change historical rows.
/// </summary>
public sealed class RecurringBooking : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Minimum recurrence interval in weeks.</summary>
    public const int MinIntervalWeeks = 1;

    /// <summary>Maximum recurrence interval in weeks (monthly ≈ 4).</summary>
    public const int MaxIntervalWeeks = 4;

    private RecurringBooking() { }

    private RecurringBooking(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        int intervalWeeks,
        DayOfWeek dayOfWeek,
        TimeOnly localStartTime,
        DateOnly startDate,
        DateOnly? endDate,
        string guestName,
        string guestEmail,
        string? guestPhone,
        string? guestNotes,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        ServiceTypeId = serviceTypeId;
        IntervalWeeks = intervalWeeks;
        DayOfWeek = dayOfWeek;
        LocalStartTime = localStartTime;
        StartDate = startDate;
        EndDate = endDate;
        GuestName = guestName;
        GuestEmail = guestEmail;
        GuestPhone = guestPhone;
        GuestNotes = guestNotes;
        Status = RecurringBookingStatus.Active;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The staff member providing the recurring service.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>The service type being booked.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>Interval between occurrences, in weeks. 1 = weekly, 2 = bi-weekly, 4 ≈ monthly.</summary>
    public int IntervalWeeks { get; private set; }

    /// <summary>The day of week the occurrences fall on.</summary>
    public DayOfWeek DayOfWeek { get; private set; }

    /// <summary>Start time of day in the tenant's local timezone.</summary>
    public TimeOnly LocalStartTime { get; private set; }

    /// <summary>First possible occurrence date (local date in tenant TZ).</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last possible occurrence date (inclusive), or null for open-ended.</summary>
    public DateOnly? EndDate { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public RecurringBookingStatus Status { get; private set; }

    /// <summary>Guest display name copied onto each generated booking.</summary>
    public string GuestName { get; private set; } = default!;

    /// <summary>Guest email copied onto each generated booking.</summary>
    public string GuestEmail { get; private set; } = default!;

    /// <summary>Optional guest phone.</summary>
    public string? GuestPhone { get; private set; }

    /// <summary>Optional guest-supplied notes.</summary>
    public string? GuestNotes { get; private set; }

    /// <summary>
    /// The last date (inclusive, in tenant TZ) for which the worker has already generated
    /// booking instances. The worker uses this watermark to resume cheaply.
    /// </summary>
    public DateOnly? LastGeneratedThrough { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of last state change.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    // -------------------------------------------------------------------------

    /// <summary>Cancels the series — the generator will stop producing new instances.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status == RecurringBookingStatus.Cancelled)
            return Result.Failure(Error.Validation("RecurringBooking.AlreadyCancelled",
                "The recurring booking is already cancelled."));

        Status = RecurringBookingStatus.Cancelled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Records the watermark up to which the worker has generated instances.</summary>
    public void AdvanceGenerationWatermark(DateOnly through, DateTimeOffset now)
    {
        if (LastGeneratedThrough is { } current && through <= current) return;
        LastGeneratedThrough = through;
        UpdatedAt = now;
    }

    // -------------------------------------------------------------------------

    /// <summary>Creates a new active recurring booking template.</summary>
    public static Result<RecurringBooking> Create(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        int intervalWeeks,
        DayOfWeek dayOfWeek,
        TimeOnly localStartTime,
        DateOnly startDate,
        DateOnly? endDate,
        string guestName,
        string guestEmail,
        string? guestPhone,
        string? guestNotes,
        DateTimeOffset now)
    {
        if (intervalWeeks < MinIntervalWeeks || intervalWeeks > MaxIntervalWeeks)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.IntervalOutOfRange",
                $"IntervalWeeks must be between {MinIntervalWeeks} and {MaxIntervalWeeks}."));

        if (endDate is { } e && e < startDate)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.EndBeforeStart",
                "EndDate cannot be before StartDate."));

        if (string.IsNullOrWhiteSpace(guestName) || guestName.Length > Booking.MaxGuestNameLength)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.GuestNameInvalid",
                $"Guest name is required and must be {Booking.MaxGuestNameLength} characters or fewer."));

        if (string.IsNullOrWhiteSpace(guestEmail) || guestEmail.Length > Booking.MaxGuestEmailLength)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.GuestEmailInvalid",
                $"Guest email is required and must be {Booking.MaxGuestEmailLength} characters or fewer."));

        if (guestPhone is not null && guestPhone.Length > Booking.MaxGuestPhoneLength)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.GuestPhoneTooLong",
                $"Guest phone must be {Booking.MaxGuestPhoneLength} characters or fewer."));

        if (guestNotes is not null && guestNotes.Length > Booking.MaxGuestNotesLength)
            return Result.Failure<RecurringBooking>(Error.Validation("RecurringBooking.GuestNotesTooLong",
                $"Notes must be {Booking.MaxGuestNotesLength} characters or fewer."));

        return new RecurringBooking(
            id,
            tenantId,
            staffId,
            serviceTypeId,
            intervalWeeks,
            dayOfWeek,
            localStartTime,
            startDate,
            endDate,
            guestName.Trim(),
            guestEmail.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(guestPhone) ? null : guestPhone.Trim(),
            string.IsNullOrWhiteSpace(guestNotes) ? null : guestNotes.Trim(),
            now);
    }
}
