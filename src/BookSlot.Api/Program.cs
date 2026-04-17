using BookSlot.Features;
using BookSlot.Features.Shared.Endpoints;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI / Swagger surface.
builder.Services.AddOpenApi();

// VSA wire-up: auto-register endpoints and validators from the Features assembly.
builder.Services.AddEndpoints(FeaturesAssemblyMarker.Assembly);
builder.Services.AddValidatorsFromAssembly(FeaturesAssemblyMarker.Assembly);

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
