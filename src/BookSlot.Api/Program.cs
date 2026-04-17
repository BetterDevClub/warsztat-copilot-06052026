using BookSlot.Api.Identity;
using BookSlot.Features;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Infrastructure;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger surface.
builder.Services.AddOpenApi();

// VSA wire-up: auto-register endpoints and validators from the Features assembly.
builder.Services.AddEndpoints(FeaturesAssemblyMarker.Assembly);
builder.Services.AddValidatorsFromAssembly(FeaturesAssemblyMarker.Assembly);

// Persistence + interceptors. Real ICurrentUser/ICurrentTenant are wired in Phase 5/6;
// the API host registers temporary no-op fallbacks so DI resolves in the walking skeleton.
builder.Services.AddSingleton<BookSlot.Domain.Abstractions.ICurrentUser, AnonymousCurrentUser>();
builder.Services.AddSingleton<BookSlot.Domain.Abstractions.ICurrentTenant, UnresolvedCurrentTenant>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapEndpoints();

app.Run();

/// <summary>Entry point type exposed for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
