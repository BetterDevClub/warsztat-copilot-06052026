using BookSlot.Worker.Jobs;
using BookSlot.Worker.Leadership;

namespace BookSlot.Worker;

/// <summary>
/// Orchestrates leader election + job dispatch. Runs every
/// <see cref="TickInterval"/>: attempts to acquire leadership (cheap no-op when
/// already leader), then walks the registered <see cref="IWorkerJob"/> set and
/// invokes any job whose next-run time has elapsed. Standby replicas idle
/// until the active leader goes away.
/// </summary>
internal sealed class JobScheduler : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILeaderElection _leaderElection;
    private readonly ILogger<JobScheduler> _logger;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, DateTimeOffset> _nextRunByJob = new(StringComparer.Ordinal);

    public JobScheduler(
        IServiceScopeFactory scopeFactory,
        ILeaderElection leaderElection,
        ILogger<JobScheduler> logger,
        TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _leaderElection = leaderElection;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job scheduler starting (tick={Tick}).", TickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick failed — will retry on next interval.");
            }

            try { await Task.Delay(TickInterval, _clock, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        await _leaderElection.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        bool acquired;
        try { acquired = await _leaderElection.TryAcquireAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Leader election attempt failed.");
            return;
        }

        if (!acquired)
        {
            if (_nextRunByJob.Count > 0) _nextRunByJob.Clear();
            return;
        }

        var now = _clock.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetServices<IWorkerJob>().ToList();

        foreach (var job in jobs)
        {
            if (!_nextRunByJob.TryGetValue(job.Name, out var due)) due = now;
            if (now < due) continue;

            try
            {
                _logger.LogInformation("Running job {Job}.", job.Name);
                await job.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {Job} threw — next run rescheduled normally.", job.Name);
            }

            _nextRunByJob[job.Name] = now + job.Interval;
        }
    }
}
