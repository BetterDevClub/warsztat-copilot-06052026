using System.Threading.RateLimiting;
using BookSlot.Features;
using BookSlot.Features.Shared.Auth;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger surface.
builder.Services.AddOpenApi();

// Rate limiting — public booking endpoints (per-IP, 10 req/min).
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

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// VSA wire-up: auto-register endpoints and validators from the Features assembly.
builder.Services.AddEndpoints(FeaturesAssemblyMarker.Assembly);
builder.Services.AddValidatorsFromAssembly(FeaturesAssemblyMarker.Assembly);

// Infrastructure first (registers JwtOptions, Identity, DbContext). Tenancy sets up
// the scoped ICurrentTenant before AddAuth replaces ICurrentUser with the real accessor.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTenancy(builder.Configuration);
builder.Services.AddAuth(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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

app.Run();

namespace BookSlot.Api
{
    /// <summary>Entry point type exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
    public partial class Program;
}
