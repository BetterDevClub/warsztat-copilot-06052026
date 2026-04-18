using Microsoft.AspNetCore.SignalR;
using BookSlot.Web.Hubs;

namespace BookSlot.Web.Notifications;

/// <summary>
/// Thin façade over <see cref="IHubContext{NotificationsHub}"/> that server-side code
/// (e.g. slice handlers, background jobs) uses to push events to connected admins.
/// Events are routed to the per-tenant group populated by <c>JoinTenantAsync</c>.
/// </summary>
public interface INotificationsPublisher
{
    Task BookingCreatedAsync(string tenantId, BookingCreatedNotification payload, CancellationToken ct = default);
    Task BroadcastAsync(string tenantId, string eventName, object payload, CancellationToken ct = default);
}

public sealed record BookingCreatedNotification(
    Guid BookingId,
    string GuestName,
    string ServiceName,
    DateTimeOffset StartUtc);

internal sealed class SignalRNotificationsPublisher : INotificationsPublisher
{
    private readonly IHubContext<NotificationsHub> _hub;
    public SignalRNotificationsPublisher(IHubContext<NotificationsHub> hub) => _hub = hub;

    public Task BookingCreatedAsync(string tenantId, BookingCreatedNotification payload, CancellationToken ct = default) =>
        _hub.Clients.Group(NotificationsHub.TenantGroup(tenantId))
            .SendAsync("booking-created", payload, ct);

    public Task BroadcastAsync(string tenantId, string eventName, object payload, CancellationToken ct = default) =>
        _hub.Clients.Group(NotificationsHub.TenantGroup(tenantId))
            .SendAsync(eventName, payload, ct);
}
