using BookSlot.Domain.Abstractions;
using BookSlot.Worker.Composition;
using BookSlot.Worker.Leadership;

namespace BookSlot.Worker;

/// <summary>Worker-host composition extensions.</summary>
public static class WorkerDependencyInjection
{
    /// <summary>
    /// Registers leader election, the scheduler, and the (initially empty)
    /// <see cref="Jobs.IWorkerJob"/> set. Individual jobs are added by the
    /// subsequent phases (21–23).
    /// </summary>
    public static IServiceCollection AddWorkerHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Worker-side principals so AuditInterceptor and tenant query filters resolve
        // — jobs opt into a specific tenant via AmbientCurrentTenant.EnterScope().
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();
        services.AddSingleton<ICurrentTenant, AmbientCurrentTenant>();

        services.AddSingleton<ILeaderElection, PostgresAdvisoryLockLeaderElection>();
        services.AddHostedService<JobScheduler>();

        // Phase 21 — booking-lifecycle jobs. Each one is scoped so it gets a
        // fresh AppDbContext per tick via JobScheduler's per-tick service scope.
        services.AddScoped<Jobs.IWorkerJob, Jobs.SlotLockCleanerJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.ReminderDispatcherJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.NoShowMarkerJob>();

        return services;
    }
}
