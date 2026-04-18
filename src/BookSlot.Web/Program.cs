using BookSlot.Domain.Abstractions;
using BookSlot.Features.Shared.Auth;
using BookSlot.Features.Shared.Tenancy;
using BookSlot.Infrastructure;
using BookSlot.Web.Account;
using BookSlot.Web.Auth;
using BookSlot.Web.Components;
using BookSlot.Web.Hubs;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Razor Components with both interactive render modes (Server + WASM).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// MudBlazor — registers MudDialog/Snackbar/Theme services consumed by the layout.
builder.Services.AddMudServices();

// Infrastructure (DbContext, IdentityCore<ApplicationUser>, Redis multiplexer, ...).
builder.Services.AddInfrastructure(builder.Configuration);

// Tenancy — registers ICurrentTenant + TenantResolutionMiddleware. We use only
// header/subdomain resolution here (cookies don't carry tenant claims yet).
builder.Services.AddTenancy(builder.Configuration);

// ICurrentUser — read from HttpContext. AddAuth would also wire JWT bearer as the
// default scheme, which we don't want in the SSR shell, so we register the accessor
// directly and let the cookie scheme provide HttpContext.User.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(
    sp => sp.GetRequiredService<CurrentUserAccessor>()));

// Cookie authentication tailored for the SSR shell. IdentityCore is already wired;
// we only need to attach a cookie scheme so SignInManager can persist identity.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "bookslot.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, PersistingServerAuthenticationStateProvider>();
builder.Services.AddScoped<IdentityRedirectManager>();

// SignalR + Redis backplane (so multi-instance deployments share hub state).
var signalR = builder.Services.AddSignalR();
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    signalR.AddStackExchangeRedis(redisConn, opts => opts.Configuration.ChannelPrefix = RedisChannel.Literal("bookslot"));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BookSlot.Web.Client._Imports).Assembly);

// SSR account endpoints (login/logout) and the notifications hub.
app.MapAccountEndpoints();
app.MapHub<NotificationsHub>("/hubs/notifications");

app.Run();
