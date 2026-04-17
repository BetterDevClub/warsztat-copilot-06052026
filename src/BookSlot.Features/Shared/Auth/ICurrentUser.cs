namespace BookSlot.Features.Shared.Auth;

/// <summary>
/// Provides information about the caller of the current request.
/// Implementations live in the host (API) and read from <c>HttpContext.User</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when the caller is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Unique id of the caller, or <c>null</c> if anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>Email of the caller, or <c>null</c> if anonymous.</summary>
    string? Email { get; }

    /// <summary>Role names assigned to the caller.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Returns true if the caller has the given role.</summary>
    bool IsInRole(string role);
}
