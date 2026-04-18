using BookSlot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BookSlot.Web.Account;

/// <summary>
/// Minimum-viable server-side login/logout surface backing the Blazor Web App shell.
/// A Blazor SSR form posts here; the endpoints exchange credentials for an Identity
/// cookie and redirect back into the SPA. Deliberately a classic POST flow — Blazor's
/// interactive render modes cannot write the auth cookie themselves.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/account").DisableAntiforgery();

        group.MapPost("/login", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var result = await signInManager.PasswordSignInAsync(
                userName: email,
                password: password,
                isPersistent: true,
                lockoutOnFailure: true).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return Results.Redirect("/account/login?error=1");
            }

            var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl!;
            return Results.LocalRedirect(target);
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync().ConfigureAwait(false);
            return Results.LocalRedirect("/");
        });
    }
}

/// <summary>
/// Helper exposed to components so SSR login forms can redirect through
/// <see cref="IHttpContextAccessor"/> without tight coupling to ASP.NET primitives.
/// </summary>
public sealed class IdentityRedirectManager
{
    private readonly IHttpContextAccessor _accessor;

    public IdentityRedirectManager(IHttpContextAccessor accessor) => _accessor = accessor;

    public void RedirectTo(string path)
    {
        var ctx = _accessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is not available — cannot redirect.");
        ctx.Response.Redirect(path);
    }
}
