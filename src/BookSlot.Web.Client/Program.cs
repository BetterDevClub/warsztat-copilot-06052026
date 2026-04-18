using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddMudBlazorAuth();

await builder.Build().RunAsync();

// Inline extension to keep Program.cs compact while still registering the required
// WASM-side authorization services (AuthenticationStateProvider + AuthorizationCore
// + role-based policies mirroring the server host).
internal static class ClientServiceRegistration
{
    // Role names kept in sync with BookSlot.Domain.Abstractions.Roles — Web.Client
    // intentionally doesn't reference Domain to keep the WASM payload slim.
    private const string Owner = "Owner";
    private const string Staff = "Staff";
    private const string Viewer = "Viewer";

    public static IServiceCollection AddMudBlazorAuth(this IServiceCollection services)
    {
        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy("RequireOwner", p => p.RequireRole(Owner));
            options.AddPolicy("RequireStaff", p => p.RequireRole(Owner, Staff));
            options.AddPolicy("RequireViewer", p => p.RequireRole(Owner, Staff, Viewer));
        });
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, BookSlot.Web.Client.PersistentAuthenticationStateProvider>();
        return services;
    }
}
