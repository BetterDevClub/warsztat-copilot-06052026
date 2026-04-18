using System.Threading.RateLimiting;
using BookSlot.Features;
using BookSlot.Features.Shared;
using BookSlot.Features.Shared.Auth;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure;
using BookSlot.Infrastructure.Observability;
using BookSlot.Infrastructure.Security;
using FluentValidation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog (console + Seq) replaces the default ILogger pipeline; OpenTelemetry feeds
// traces + metrics over OTLP so dev runs surface end-to-end timings.
builder.Host.UseBookSlotSerilog("BookSlot.Api");
builder.Services.AddBookSlotOpenTelemetry(builder.Configuration, "BookSlot.Api");

// OpenAPI / Swagger surface.
builder.Services.AddOpenApi();

// Rate limiting — public booking endpoints (per-IP, 10 req/min) and sensitive
// auth endpoints (login/refresh, per-IP, 5 req/min) so brute-force attempts
// hit a 429 long before lockout kicks in.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("bookings-public", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.AddPolicy("auth-sensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS for browser callers (booking widgets); origins come from Cors:AllowedOrigins.
builder.Services.AddBookSlotCors(builder.Configuration, builder.Environment);

// VSA wire-up: auto-register endpoints, handlers and validators from the Features assembly.
builder.Services.AddEndpoints(FeaturesAssemblyMarker.Assembly);
builder.Services.AddFeatureHandlers();
builder.Services.AddValidatorsFromAssembly(FeaturesAssemblyMarker.Assembly);

// Infrastructure first (registers JwtOptions, Identity, DbContext). Tenancy sets up
// the scoped ICurrentTenant before AddAuth replaces ICurrentUser with the real accessor.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTenancy(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);

// Fail-fast on Production startup if a placeholder/dev secret leaked into config.
builder.Services.AddHostedService<ProductionSecretsValidator>();

// Probe surface: /health (full report), /health/ready (deps), /health/live (process up).
builder.Services.AddBookSlotHealthChecks(builder.Configuration);

var app = builder.Build();

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseBookSlotCors();

// Authentication must run before tenant resolution so the tenant slug claim is
// available to the middleware (priority: claim > subdomain > header).
app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
app.UseRateLimiter();

// Public endpoints mount on root; tenant-scoped endpoints mount on /api/v1 with RequireTenantFilter.
var tenantGroup = app.MapGroup("/api/v1")
    .AddEndpointFilter<RequireTenantFilter>()
    .WithTags("v1");

app.MapEndpoints(tenantGroup: tenantGroup);
app.MapBookSlotHealthChecks();

app.Run();

namespace BookSlot.Api
{
    /// <summary>Entry point type exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
    public partial class Program;
}
