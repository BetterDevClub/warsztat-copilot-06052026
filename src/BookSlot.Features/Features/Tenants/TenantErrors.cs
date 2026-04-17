using BookSlot.Domain.Primitives;

namespace BookSlot.Features.Tenants;

/// <summary>Shared error codes for the Tenants slices.</summary>
internal static class TenantErrors
{
    /// <summary>Raised when the requested slug is already in use by another tenant.</summary>
    public static readonly Error SlugTaken = Error.Conflict(
        "Tenants.SlugTaken",
        "The requested tenant slug is already in use.");

    /// <summary>Raised when tenant settings cannot be located for the resolved tenant.</summary>
    public static readonly Error SettingsNotFound = Error.NotFound(
        "Tenants.SettingsNotFound",
        "Settings for the current tenant were not found.");
}
