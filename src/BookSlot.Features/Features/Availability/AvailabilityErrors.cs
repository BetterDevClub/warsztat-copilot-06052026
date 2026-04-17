using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Availability;

/// <summary>Domain errors for the Availability feature group.</summary>
internal static class AvailabilityErrors
{
    /// <summary>The requested service type does not exist or is inactive.</summary>
    internal static readonly Error ServiceTypeNotFound =
        Error.NotFound("Availability.ServiceTypeNotFound", "Service type not found or is no longer active.");

    /// <summary>The requested staff member does not exist or is inactive.</summary>
    internal static readonly Error StaffNotFound =
        Error.NotFound("Availability.StaffNotFound", "Staff member not found or is no longer active.");

    /// <summary>The staff member cannot perform the requested service type.</summary>
    internal static readonly Error ServiceNotAssigned =
        Error.Validation("Availability.ServiceNotAssigned", "This staff member is not assigned to the requested service type.");
}
