using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Periodically purges <see cref="Domain.Reservations.SlotReservation"/> rows whose
/// TTL elapsed without conversion to a <see cref="Domain.Bookings.Booking"/>.
/// Safe to run concurrently with the checkout flow — the booking creation handler
/// deletes its own reservation inside the same transaction, so any row still
/// present past its <c>ExpiresAtUtc</c> is guaranteed to be unconsumed.
/// </summary>
internal sealed class SlotLockCleanerJob : IWorkerJob
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<SlotLockCleanerJob> _logger;

    public SlotLockCleanerJob(AppDbContext db, TimeProvider clock, ILogger<SlotLockCleanerJob> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "slot-lock-cleaner";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var deleted = await _db.SlotReservations
            .IgnoreQueryFilters()
            .Where(r => r.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            _logger.LogInformation("SlotLockCleaner removed {Count} expired reservations.", deleted);
        }
    }
}
