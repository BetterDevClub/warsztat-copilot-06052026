namespace BookSlot.Worker.Jobs;

/// <summary>
/// Contract for scheduled worker jobs. Implementations are resolved from DI
/// as scoped services and invoked by <see cref="JobScheduler"/> on their
/// declared interval — but only on the leader instance.
/// </summary>
public interface IWorkerJob
{
    /// <summary>Stable identifier used for logging and metrics.</summary>
    string Name { get; }

    /// <summary>How frequently <see cref="RunAsync"/> should be invoked.</summary>
    TimeSpan Interval { get; }

    /// <summary>Executes the job. Exceptions are caught and logged by the scheduler.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
