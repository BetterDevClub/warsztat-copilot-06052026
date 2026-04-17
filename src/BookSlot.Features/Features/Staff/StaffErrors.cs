using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Staff;

/// <summary>Shared error definitions for the Staff feature.</summary>
internal static class StaffErrors
{
    /// <summary>Staff member not found for the current tenant.</summary>
    public static readonly Error NotFound = Error.NotFound("Staff.NotFound", "Staff member was not found.");

    /// <summary>One or more service types referenced in a bulk assignment don't exist in the tenant.</summary>
    public static readonly Error ServiceTypesNotFound = Error.Validation(
        "Staff.ServiceTypesNotFound",
        "One or more service types were not found for the current tenant.");

    /// <summary>An override already exists for the given staff + date (unavailable override is unique per day).</summary>
    public static readonly Error OverrideConflict = Error.Conflict(
        "Staff.OverrideConflict",
        "An override for this staff member already exists on this date.");

    /// <summary>Availability override not found.</summary>
    public static readonly Error OverrideNotFound = Error.NotFound(
        "Staff.OverrideNotFound",
        "Availability override was not found.");
}
