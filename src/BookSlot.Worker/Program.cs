using BookSlot.Infrastructure;
using BookSlot.Infrastructure.Observability;
using BookSlot.Worker;
using BookSlot.Worker.Leadership;

var builder = WebApplication.CreateBuilder(args);

// Serilog + OpenTelemetry — same plumbing as Api/Web for unified dev dashboards.
builder.Host.UseBookSlotSerilog("BookSlot.Worker");
builder.Services.AddBookSlotOpenTelemetry(builder.Configuration, "BookSlot.Worker");

// Share the same infrastructure registrations as the API (DbContext, Redis,
// notifications, integrations) so worker jobs can use the full feature stack.
builder.Services.AddInfrastructure(builder.Configuration);
// SignInManager (pulled in transitively by AddIdentityCore().AddSignInManager())
// depends on IAuthenticationSchemeProvider and IDataProtectionProvider even though
// the worker never signs anyone in — register the minimum plumbing to satisfy DI.
builder.Services.AddAuthentication();
builder.Services.AddDataProtection();
builder.Services.AddWorkerHost();

// Health checks: shared (Postgres/Redis/Outbox) + worker-specific leader probe.
builder.Services
    .AddBookSlotHealthChecks(builder.Configuration)
    .AddCheck<LeaderElectionHealthCheck>("leader-election", tags: new[] { "ready", "leader" });

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "BookSlot.Worker", status = "running" }));
app.MapBookSlotHealthChecks();

app.Run();

/// <summary>Liveness probe that reports whether this replica is the elected leader.</summary>
internal sealed class LeaderElectionHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly ILeaderElection _election;

    public LeaderElectionHealthCheck(ILeaderElection election) => _election = election;

    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Both leader and standby are "healthy" from an orchestrator's perspective —
        // only report the role in the metadata so dashboards can distinguish them.
        var data = new Dictionary<string, object> { ["role"] = _election.IsLeader ? "leader" : "standby" };
        return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
            description: _election.IsLeader ? "Leader" : "Standby",
            data: data));
    }
}
