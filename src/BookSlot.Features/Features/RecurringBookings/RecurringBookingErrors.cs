using BookSlot.Domain.Primitives;

namespace BookSlot.Features.RecurringBookings;

/// <summary>Feature-layer errors for the RecurringBookings group.</summary>
internal static class RecurringBookingErrors
{
    internal static readonly Error NotFound =
        Error.NotFound("RecurringBooking.NotFound", "Recurring booking not found.");

    internal static readonly Error StaffNotFound =
        Error.NotFound("RecurringBooking.StaffNotFound", "Staff member not found or inactive.");

    internal static readonly Error ServiceTypeNotFound =
        Error.NotFound("RecurringBooking.ServiceTypeNotFound", "Service type not found or inactive.");
}
