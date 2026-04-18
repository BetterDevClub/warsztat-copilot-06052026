using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace BookSlot.Infrastructure.Observability;

/// <summary>
/// Wires Serilog (console + Seq), OpenTelemetry (traces + metrics + logs over OTLP)
/// and the BookSlot health check suite into any host (<see cref="IHostBuilder"/> +
/// <see cref="IServiceCollection"/>). Designed so Api, Web, and Worker share the
/// exact same telemetry pipeline — only the <c>serviceName</c> resource attribute
/// differs.
/// </summary>
public static class ObservabilityExtensions
{
    private const string SeqDefaultUrl = "http://localhost:5341";

    /// <summary>
    /// Replaces the default logger factory with Serilog reading its
    /// settings from configuration (<c>Serilog</c> section), enriched with
    /// process / machine / thread / environment data and a Seq sink fed from
    /// <c>ConnectionStrings:Seq</c> (defaults to <c>http://localhost:5341</c>).
    /// </summary>
    public static IHostBuilder UseBookSlotSerilog(this IHostBuilder host, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return host.UseSerilog((ctx, services, lc) =>
        {
            var seqUrl = ctx.Configuration.GetConnectionString("Seq") ?? SeqDefaultUrl;

            lc.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Service", serviceName)
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {Message:lj}{NewLine}{Exception}")
                .WriteTo.Seq(seqUrl);
        });
    }

    /// <summary>
    /// Registers OpenTelemetry tracing + metrics with AspNetCore, HttpClient and
    /// Runtime instrumentations plus an OTLP exporter pointed at
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (env var, default
    /// <c>http://localhost:5341/ingest/otlp</c> — Seq's OTLP ingest endpoint).
    /// </summary>
    public static IServiceCollection AddBookSlotOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["Otlp:Endpoint"];
        var useHttpProtobuf = string.Equals(
            configuration["Otlp:Protocol"] ?? "HttpProtobuf",
            "HttpProtobuf",
            StringComparison.OrdinalIgnoreCase);

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment",
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"),
            });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "1.0.0"))
            .WithTracing(t =>
            {
                t.SetResourceBuilder(resource)
                    .AddSource(BookSlotActivitySource.Name)
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    t.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        if (useHttpProtobuf) o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            })
            .WithMetrics(m =>
            {
                m.SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    m.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        if (useHttpProtobuf) o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });

        return services;
    }

    /// <summary>
    /// Registers Postgres + Redis + outbox-lag health checks. Connection strings
    /// are resolved from configuration so each host (Api/Web/Worker) only needs
    /// to call this once.
    /// </summary>
    public static IHealthChecksBuilder AddBookSlotHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddHealthChecks();

        var postgres = configuration.GetConnectionString(DependencyInjection.PostgresConnectionStringName);
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.AddNpgSql(postgres, name: "postgres", tags: new[] { "ready", "db" });
        }

        var redis = configuration.GetConnectionString(DependencyInjection.RedisConnectionStringName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.AddRedis(redis, name: "redis", tags: new[] { "ready", "cache" });
        }

        builder.AddCheck<OutboxLagHealthCheck>(
            name: "outbox-lag",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "outbox" });

        return builder;
    }

    /// <summary>
    /// Maps the standard probe surface:
    /// <c>/health/live</c> — liveness (no checks; only "process is up"),
    /// <c>/health/ready</c> — readiness (all checks tagged <c>ready</c>),
    /// <c>/health</c> — full report (all checks, JSON via UIResponseWriter).
    /// </summary>
    public static IEndpointRouteBuilder MapBookSlotHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        return endpoints;
    }
}
