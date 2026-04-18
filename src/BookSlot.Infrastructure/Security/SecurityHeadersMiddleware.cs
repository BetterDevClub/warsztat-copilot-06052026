using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Infrastructure.Security;

/// <summary>
/// Adds the standard browser-protection headers to every response. Designed to be
/// safe for both the API (JSON) and the Blazor SSR shell — the CSP is permissive
/// enough to allow Blazor's inline bootstrap script and SignalR but blocks
/// arbitrary third-party origins.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var headers = ctx.Response.Headers;

            // Prevent MIME sniffing — uploaded blobs always honoured by their declared type.
            headers["X-Content-Type-Options"] = "nosniff";
            // Legacy clickjacking guard; CSP frame-ancestors below covers modern browsers.
            headers["X-Frame-Options"] = "DENY";
            // Strip referrer when navigating cross-origin to avoid leaking tenant slugs.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // Disable powerful APIs we don't intentionally use.
            headers["Permissions-Policy"] =
                "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()";
            // Cross-origin isolation hints (safe defaults; do not require COEP).
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // CSP: allow Blazor's inline bootstrap script + WebAssembly + SignalR; deny framing.
            // 'unsafe-inline' on style-src keeps MudBlazor + Razor scoped CSS working without nonces.
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' 'wasm-unsafe-eval' 'unsafe-inline'; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data: https:; " +
                    "font-src 'self' data:; " +
                    "connect-src 'self' ws: wss:; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self'";
            }

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
