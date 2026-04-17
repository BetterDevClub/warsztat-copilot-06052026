namespace BookSlot.Domain.Abstractions;

/// <summary>
/// Provides information about the caller of the current unit of work.
/// Implementations live in the host layer (API: read from <c>HttpContext.User</c>;
/// Worker: from the job's execution context or a system principal).
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
