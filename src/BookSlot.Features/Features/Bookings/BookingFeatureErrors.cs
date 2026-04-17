using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Bookings;

/// <summary>Feature-layer errors for the Bookings group.</summary>
internal static class BookingFeatureErrors
{
    internal static readonly Error ReservationNotFound =
        Error.NotFound("Booking.ReservationNotFound", "Reservation not found or has already expired.");

    internal static readonly Error ReservationExpired =
        Error.Conflict("Booking.ReservationExpired", "The slot reservation has expired. Please start over.");

    internal static readonly Error ConcurrencyConflict =
        Error.Conflict("Booking.ConcurrencyConflict", "This slot was taken while you were booking. Please try a different time.");

    internal static readonly Error BookingNotFound =
        Error.NotFound("Booking.NotFound", "Booking not found.");

    internal static readonly Error InvalidCancelToken =
        Error.Validation("Booking.InvalidCancelToken", "Invalid or expired cancellation token.");

    internal static readonly Error InvalidRescheduleToken =
        Error.Validation("Booking.InvalidRescheduleToken", "Invalid or expired reschedule token.");

    internal static readonly Error ServiceTypeNotFound =
        Error.NotFound("Booking.ServiceTypeNotFound", "Service type not found or is no longer active.");

    internal static readonly Error StaffNotFound =
        Error.NotFound("Booking.StaffNotFound", "Staff member not found or is no longer active.");
}
