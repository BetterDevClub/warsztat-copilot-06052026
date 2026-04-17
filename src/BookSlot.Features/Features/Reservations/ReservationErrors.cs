using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Reservations;

/// <summary>Domain errors for the Reservations feature group.</summary>
internal static class ReservationErrors
{
    /// <summary>The requested slot is already reserved by another guest.</summary>
    internal static readonly Error SlotAlreadyReserved =
        Error.Conflict("Reservation.SlotAlreadyReserved", "This slot is already reserved. Please choose another time.");

    /// <summary>Could not acquire the distributed lock — contention too high.</summary>
    internal static readonly Error LockContention =
        Error.Conflict("Reservation.LockContention", "The slot is momentarily locked by another request. Please try again.");

    /// <summary>Reservation not found or belongs to a different tenant.</summary>
    internal static readonly Error NotFound =
        Error.NotFound("Reservation.NotFound", "Reservation not found.");

    /// <summary>The provided guest token does not match the reservation.</summary>
    internal static readonly Error InvalidToken =
        Error.Validation("Reservation.InvalidToken", "Invalid or expired reservation token.");
}
