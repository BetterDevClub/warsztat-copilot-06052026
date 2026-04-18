using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BookSlot.Web.Hubs;

/// <summary>
/// Authenticated SignalR hub streaming real-time events to the admin UI —
/// new bookings, cancellations and webhook delivery failures. The Redis
/// backplane (registered in <c>Program.cs</c>) fans messages out across
/// web replicas so any subscriber sees every event exactly once.
/// Clients call <see cref="JoinTenantAsync"/> after connecting to subscribe
/// to their own tenant group, which all server-side publishers broadcast to.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    /// <summary>Adds the caller to a per-tenant group so publishers can target it.</summary>
    public Task JoinTenantAsync(string tenantId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

    /// <summary>Removes the caller from their tenant group.</summary>
    public Task LeaveTenantAsync(string tenantId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

    /// <summary>Stable group name format — publishers use the same helper.</summary>
    public static string TenantGroup(string tenantId) => $"tenant:{tenantId}";
}
