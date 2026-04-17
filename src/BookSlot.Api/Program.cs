using BookSlot.Api.Identity;
using BookSlot.Features;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger surface.
builder.Services.AddOpenApi();

// VSA wire-up: auto-register endpoints and validators from the Features assembly.
builder.Services.AddEndpoints(FeaturesAssemblyMarker.Assembly);
builder.Services.AddValidatorsFromAssembly(FeaturesAssemblyMarker.Assembly);

// Multi-tenancy: scoped ICurrentTenant + header/subdomain resolution middleware.
// AddTenancy replaces any earlier ICurrentTenant registration with the scoped accessor.
builder.Services.AddSingleton<BookSlot.Domain.Abstractions.ICurrentUser, AnonymousCurrentUser>();
builder.Services.AddTenancy(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseTenantResolution();

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
