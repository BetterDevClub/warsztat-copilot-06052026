using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Bookings;

/// <summary>Error constants for the Booking aggregate.</summary>
internal static class BookingErrors
{
    internal static Error CannotCancelInStatus(BookingStatus status) =>
        Error.Validation("Booking.CannotCancel",
            $"A booking in status '{status}' cannot be cancelled.");

    internal static Error CannotRescheduleInStatus(BookingStatus status) =>
        Error.Validation("Booking.CannotReschedule",
            $"A booking in status '{status}' cannot be rescheduled.");

    internal static Error CannotMarkNoShowInStatus(BookingStatus status) =>
        Error.Validation("Booking.CannotMarkNoShow",
            $"Only confirmed bookings can be marked as no-show (current status: '{status}').");

    internal static readonly Error NotesTooMany =
        Error.Validation("BookingNote.TooMany",
            $"A booking can have at most {BookingNote.MaxNotesPerBooking} notes.");
}
