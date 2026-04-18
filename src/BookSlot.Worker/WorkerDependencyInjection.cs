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

        // HttpClient factory for outbound integrations (webhook delivery, Google OAuth).
        services.AddHttpClient();

        // Phase 21 — booking-lifecycle jobs. Each one is scoped so it gets a
        // fresh AppDbContext per tick via JobScheduler's per-tick service scope.
        services.AddScoped<Jobs.IWorkerJob, Jobs.SlotLockCleanerJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.ReminderDispatcherJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.NoShowMarkerJob>();

        // Phase 22 — integration jobs.
        services.AddScoped<Jobs.IWorkerJob, Jobs.OutboxFanoutJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.WebhookDeliveryJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.GoogleCalendarTokenRefreshJob>();

        // Phase 23 — scheduled jobs (per-tenant TZ-aware).
        services.AddScoped<Jobs.IWorkerJob, Jobs.DailyDigestSenderJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.RecurringBookingGeneratorJob>();
        services.AddScoped<Jobs.IWorkerJob, Jobs.MonthlyReportArchiverJob>();

        return services;
    }
}
