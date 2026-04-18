using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddMudBlazorAuth();

await builder.Build().RunAsync();

// Inline extension to keep Program.cs compact while still registering the required
// WASM-side authorization services (AuthenticationStateProvider + AuthorizationCore).
internal static class ClientServiceRegistration
{
    public static IServiceCollection AddMudBlazorAuth(this IServiceCollection services)
    {
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, BookSlot.Web.Client.PersistentAuthenticationStateProvider>();
        return services;
    }
}
