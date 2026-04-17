using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Bookings;

/// <summary>
/// A confirmed or pending appointment between a guest and a staff member.
/// Booking is the central aggregate of BookSlot — all flow branches (cancel, reschedule,
/// no-show) are modelled as state transitions on this entity.
/// </summary>
public sealed class Booking : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Maximum length of guest display name.</summary>
    public const int MaxGuestNameLength = 200;

    /// <summary>Maximum length of guest email.</summary>
    public const int MaxGuestEmailLength = 256;

    /// <summary>Maximum length of guest phone.</summary>
    public const int MaxGuestPhoneLength = 30;

    /// <summary>Maximum length of the guest-supplied booking note.</summary>
    public const int MaxGuestNotesLength = 1000;

    /// <summary>Maximum length of internal staff note.</summary>
    public const int MaxInternalNotesLength = 2000;

    private Booking() { }

    private Booking(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string guestName,
        string guestEmail,
        string? guestPhone,
        string? guestNotes,
        Guid? rescheduledFromId,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        ServiceTypeId = serviceTypeId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        GuestName = guestName;
        GuestEmail = guestEmail;
        GuestPhone = guestPhone;
        GuestNotes = guestNotes;
        RescheduledFromId = rescheduledFromId;
        Status = BookingStatus.Confirmed;
        CancelToken = Guid.NewGuid();
        RescheduleToken = Guid.NewGuid();
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The staff member providing the service.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>The service type being booked.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>Inclusive start of the appointment in UTC.</summary>
    public DateTimeOffset StartUtc { get; private set; }

    /// <summary>Exclusive end (start + duration + buffers) in UTC.</summary>
    public DateTimeOffset EndUtc { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public BookingStatus Status { get; private set; }

    /// <summary>Guest display name.</summary>
    public string GuestName { get; private set; } = default!;

    /// <summary>Guest contact email.</summary>
    public string GuestEmail { get; private set; } = default!;

    /// <summary>Optional guest phone number.</summary>
    public string? GuestPhone { get; private set; }

    /// <summary>Optional note from the guest (visible to staff).</summary>
    public string? GuestNotes { get; private set; }

    /// <summary>Internal note added by staff (not visible to guest).</summary>
    public string? InternalNotes { get; private set; }

    /// <summary>Opaque GUID token sent to the guest for self-service cancellation.</summary>
    public Guid CancelToken { get; private set; }

    /// <summary>Opaque GUID token sent to the guest for self-service rescheduling.</summary>
    public Guid RescheduleToken { get; private set; }

    /// <summary>When not null, this booking replaced the booking with this id.</summary>
    public Guid? RescheduledFromId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of last state change.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>PostgreSQL xmin — used as an EF optimistic concurrency token.</summary>
    public uint Xmin { get; private set; }

    /// <summary>
    /// Optional JSON document with the guest's answers to custom fields defined
    /// on the <see cref="Domain.Services.ServiceType.FormSchemaJson"/> schema.
    /// Stored as jsonb for future querying in reports.
    /// </summary>
    public string? CustomFieldValuesJson { get; private set; }

    /// <summary>Attaches (or replaces) the custom field values JSON document.</summary>
    public void SetCustomFieldValues(string? json, DateTimeOffset now)
    {
        CustomFieldValuesJson = string.IsNullOrWhiteSpace(json) ? null : json;
        UpdatedAt = now;
    }

    // -------------------------------------------------------------------------
    // Domain methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cancels the booking. Only <see cref="BookingStatus.Pending"/> and
    /// <see cref="BookingStatus.Confirmed"/> bookings may be cancelled.
    /// </summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Result.Failure(BookingErrors.CannotCancelInStatus(Status));

        Status = BookingStatus.Cancelled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Marks the booking as <see cref="BookingStatus.Rescheduled"/> when the guest
    /// confirms a new time. Called on the original booking.
    /// </summary>
    public Result MarkRescheduled(DateTimeOffset now)
    {
        if (Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            return Result.Failure(BookingErrors.CannotRescheduleInStatus(Status));

        Status = BookingStatus.Rescheduled;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Marks the booking as <see cref="BookingStatus.NoShow"/>. Admin-only.</summary>
    public Result MarkNoShow(DateTimeOffset now)
    {
        if (Status is not BookingStatus.Confirmed)
            return Result.Failure(BookingErrors.CannotMarkNoShowInStatus(Status));

        Status = BookingStatus.NoShow;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Replaces the internal staff note (admin-only).</summary>
    public void SetInternalNotes(string? notes, DateTimeOffset now)
    {
        InternalNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = now;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>Creates a new confirmed booking. Validates required fields.</summary>
    public static Result<Booking> Create(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        string guestName,
        string guestEmail,
        string? guestPhone,
        string? guestNotes,
        Guid? rescheduledFromId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(guestName) || guestName.Length > MaxGuestNameLength)
            return Result.Failure<Booking>(Error.Validation("Booking.GuestNameInvalid",
                $"Guest name is required and must be {MaxGuestNameLength} characters or fewer."));

        if (string.IsNullOrWhiteSpace(guestEmail) || guestEmail.Length > MaxGuestEmailLength)
            return Result.Failure<Booking>(Error.Validation("Booking.GuestEmailInvalid",
                $"Guest email is required and must be {MaxGuestEmailLength} characters or fewer."));

        if (guestPhone is not null && guestPhone.Length > MaxGuestPhoneLength)
            return Result.Failure<Booking>(Error.Validation("Booking.GuestPhoneTooLong",
                $"Guest phone must be {MaxGuestPhoneLength} characters or fewer."));

        if (guestNotes is not null && guestNotes.Length > MaxGuestNotesLength)
            return Result.Failure<Booking>(Error.Validation("Booking.GuestNotesTooLong",
                $"Notes must be {MaxGuestNotesLength} characters or fewer."));

        return new Booking(
            id,
            tenantId,
            staffId,
            serviceTypeId,
            startUtc,
            endUtc,
            guestName.Trim(),
            guestEmail.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(guestPhone) ? null : guestPhone.Trim(),
            string.IsNullOrWhiteSpace(guestNotes) ? null : guestNotes.Trim(),
            rescheduledFromId,
            now);
    }
}
