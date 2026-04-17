using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Reservations;

/// <summary>
/// A short-lived hold on a time slot, preventing double-booking during the checkout flow.
/// Created when a guest selects a slot; auto-expires after <see cref="TtlMinutes"/> minutes.
/// Converted to a <c>Booking</c> on payment / confirmation, or released explicitly by the guest
/// or by the <c>SlotLockCleaner</c> background job.
/// </summary>
public sealed class SlotReservation : Entity<Guid>, ITenantScoped
{
    /// <summary>How long a reservation lives before it expires.</summary>
    public const int TtlMinutes = 10;

    private SlotReservation() { }

    private SlotReservation(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset expiresAtUtc,
        Guid guestToken,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        StaffId = staffId;
        ServiceTypeId = serviceTypeId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        ExpiresAtUtc = expiresAtUtc;
        GuestToken = guestToken;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The staff member whose calendar is being held.</summary>
    public Guid StaffId { get; private set; }

    /// <summary>The service type being booked.</summary>
    public Guid ServiceTypeId { get; private set; }

    /// <summary>Inclusive start of the reserved slot in UTC.</summary>
    public DateTimeOffset StartUtc { get; private set; }

    /// <summary>Exclusive end of the reserved slot in UTC.</summary>
    public DateTimeOffset EndUtc { get; private set; }

    /// <summary>UTC timestamp after which this reservation is considered abandoned.</summary>
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Opaque token given to the guest so they can release their own reservation.</summary>
    public Guid GuestToken { get; private set; }

    /// <summary>UTC timestamp when the reservation was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True when the reservation has not yet expired.</summary>
    public bool IsActive(DateTimeOffset now) => now < ExpiresAtUtc;

    /// <summary>Factory. Sets expiry to <c>now + <see cref="TtlMinutes"/></c>.</summary>
    public static SlotReservation Create(
        Guid id,
        Guid tenantId,
        Guid staffId,
        Guid serviceTypeId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset now)
    {
        return new SlotReservation(
            id,
            tenantId,
            staffId,
            serviceTypeId,
            startUtc,
            endUtc,
            expiresAtUtc: now.AddMinutes(TtlMinutes),
            guestToken: Guid.NewGuid(),
            createdAt: now);
    }
}
