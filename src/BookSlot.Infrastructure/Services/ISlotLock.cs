namespace BookSlot.Infrastructure.Services;

/// <summary>
/// Thin abstraction over a distributed slot lock backed by Redis.
/// A lock prevents two concurrent requests from both seeing "no active reservation"
/// and writing duplicates. The lock is held only for the duration of the DB write
/// (milliseconds), not for the entire TTL of the reservation.
/// </summary>
public interface ISlotLock
{
    /// <summary>
    /// Tries to acquire an exclusive lock identified by <paramref name="key"/>.
    /// Returns a disposable <see cref="ISlotLockHandle"/> if the lock was acquired,
    /// or <see langword="null"/> if another caller already holds it.
    /// Dispose the handle to release the lock.
    /// </summary>
    Task<ISlotLockHandle?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
}

/// <summary>Represents ownership of an acquired slot lock. Dispose to release.</summary>
public interface ISlotLockHandle : IAsyncDisposable { }
