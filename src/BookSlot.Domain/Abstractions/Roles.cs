namespace BookSlot.Domain.Abstractions;

/// <summary>Canonical role names used across authorization policies and JWT claims.</summary>
public static class Roles
{
    /// <summary>Tenant owner — full administrative authority inside the tenant.</summary>
    public const string Owner = "Owner";

    /// <summary>Operational staff — can manage bookings, staff, and availability.</summary>
    public const string Staff = "Staff";

    /// <summary>Read-only access — reports, dashboards.</summary>
    public const string Viewer = "Viewer";

    /// <summary>All supported roles, seeded on startup.</summary>
    public static IReadOnlyList<string> All { get; } = [Owner, Staff, Viewer];
}
