namespace BookSlot.Worker.Leadership;

/// <summary>
/// Cluster-wide leader election primitive. Only one worker instance in the
/// cluster should execute scheduled jobs at a time; every other instance
/// stays on hot standby and takes over if the leader crashes.
/// </summary>
public interface ILeaderElection
{
    /// <summary>True when this instance currently holds the leadership lock.</summary>
    bool IsLeader { get; }

    /// <summary>
    /// Attempts to acquire (or refresh) the leadership lock. Safe to call
    /// repeatedly — a no-op when this instance is already the leader and
    /// the underlying session is still healthy.
    /// </summary>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken);

    /// <summary>Releases the lock if held. Called during graceful shutdown.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken);
}
