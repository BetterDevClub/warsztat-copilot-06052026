namespace BookSlot.Web.Notifications;

/// <summary>
/// In-process pub/sub used by InteractiveServer Blazor components to refresh
/// themselves when bookings change. Mirrors the events sent over SignalR but
/// stays inside the current process — components subscribe via <see cref="Subscribe"/>
/// and dispose the returned token to unsubscribe.
/// </summary>
public interface IBookingEventBus
{
    IDisposable Subscribe(string tenantId, Func<BookingEvent, Task> handler);
    Task PublishAsync(string tenantId, BookingEvent evt, CancellationToken ct = default);
}

public sealed record BookingEvent(string Kind, Guid? BookingId, object? Payload);

internal sealed class InMemoryBookingEventBus : IBookingEventBus
{
    private readonly Dictionary<string, List<Subscription>> _subs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public IDisposable Subscribe(string tenantId, Func<BookingEvent, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(handler);
        var sub = new Subscription(this, tenantId, handler);
        lock (_gate)
        {
            if (!_subs.TryGetValue(tenantId, out var list))
            {
                list = [];
                _subs[tenantId] = list;
            }
            list.Add(sub);
        }
        return sub;
    }

    public async Task PublishAsync(string tenantId, BookingEvent evt, CancellationToken ct = default)
    {
        Subscription[] snapshot;
        lock (_gate)
        {
            if (!_subs.TryGetValue(tenantId, out var list) || list.Count == 0) return;
            snapshot = [.. list];
        }
        foreach (var s in snapshot)
        {
            try { await s.Handler(evt).ConfigureAwait(false); }
            catch { /* never let one subscriber break the rest */ }
        }
    }

    private void Remove(Subscription sub)
    {
        lock (_gate)
        {
            if (_subs.TryGetValue(sub.TenantId, out var list))
            {
                list.Remove(sub);
                if (list.Count == 0) _subs.Remove(sub.TenantId);
            }
        }
    }

    private sealed class Subscription(InMemoryBookingEventBus owner, string tenantId, Func<BookingEvent, Task> handler) : IDisposable
    {
        public string TenantId { get; } = tenantId;
        public Func<BookingEvent, Task> Handler { get; } = handler;
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(this);
        }
    }
}
