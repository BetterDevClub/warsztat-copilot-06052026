using Microsoft.AspNetCore.Identity;

namespace BookSlot.Infrastructure.Identity;

/// <summary>Identity role record — one of <c>Owner</c>, <c>Staff</c>, <c>Viewer</c>.</summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Parameterless constructor for EF materialisation.</summary>
    public ApplicationRole() { }

    /// <summary>Creates a role with the given name.</summary>
    public ApplicationRole(string name) : base(name) { }
}
