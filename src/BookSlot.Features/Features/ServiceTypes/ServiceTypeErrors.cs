using BookSlot.Domain.Primitives;

namespace BookSlot.Features.ServiceTypes;

/// <summary>Shared error definitions for the ServiceTypes feature.</summary>
internal static class ServiceTypeErrors
{
    /// <summary>A service with the same slug already exists for the tenant.</summary>
    public static readonly Error SlugTaken = Error.Conflict(
        "ServiceType.SlugTaken",
        "A service with this slug already exists for the tenant.");

    /// <summary>No service type matches the supplied id within the current tenant.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "ServiceType.NotFound",
        "Service type was not found.");
}
