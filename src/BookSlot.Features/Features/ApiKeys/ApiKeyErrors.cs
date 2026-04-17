using BookSlot.Domain.Primitives;

namespace BookSlot.Features.ApiKeys;

/// <summary>Shared error codes for the API keys slices.</summary>
internal static class ApiKeyErrors
{
    /// <summary>Raised when the caller is not authenticated (should be caught by the auth pipeline first).</summary>
    public static readonly Error Unauthenticated =
        Error.Unauthorized("ApiKeys.Unauthenticated", "Authentication is required.");

    /// <summary>Raised when no API key with the supplied id exists for the current tenant.</summary>
    public static readonly Error NotFound =
        Error.NotFound("ApiKeys.NotFound", "API key not found.");
}
