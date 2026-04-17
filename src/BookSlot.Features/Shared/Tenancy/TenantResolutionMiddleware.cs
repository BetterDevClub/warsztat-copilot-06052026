using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookSlot.Features.Shared.Tenancy;

/// <summary>
/// Resolves the current tenant for the incoming request. Priority:
/// <list type="number">
///   <item>JWT claim on <see cref="HttpContext.User"/> (claim type from options).</item>
///   <item>First DNS label of the request <c>Host</c> when it ends with a configured root domain.</item>
///   <item>The <c>X-Tenant-Slug</c> (or configured) request header.</item>
/// </list>
/// If nothing resolves, the tenant stays unresolved — <see cref="RequireTenantFilter"/>
/// decides whether that is acceptable for the endpoint.
/// </summary>
public sealed partial class TenantResolutionMiddleware : IMiddleware
{
    private readonly CurrentTenantAccessor _accessor;
    private readonly TenantResolutionOptions _options;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    /// <summary>Creates the middleware. All dependencies are resolved from DI.</summary>
    public TenantResolutionMiddleware(
        CurrentTenantAccessor accessor,
        IOptions<TenantResolutionOptions> options,
        ILogger<TenantResolutionMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _accessor = accessor;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (TryResolve(context, out var slug))
        {
            _accessor.Set(TenantIdFactory.FromSlug(slug!), slug!);
            _logger.LogDebug("Resolved tenant '{Slug}' for request {Path}", slug, context.Request.Path);
        }

        await next(context);
    }

    private bool TryResolve(HttpContext context, out string? slug)
    {
        // 1. JWT claim.
        var claim = context.User?.FindFirst(_options.SlugClaimType)?.Value;
        if (IsValidSlug(claim))
        {
            slug = claim;
            return true;
        }

        // 2. Subdomain.
        var host = context.Request.Host.Host;
        foreach (var root in _options.RootDomains)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase)
                && host.Length > root.Length + 1)
            {
                var label = host[..^(root.Length + 1)];
                var firstLabel = label.Split('.', 2)[0];
                if (IsValidSlug(firstLabel) && !IsReserved(firstLabel))
                {
                    slug = firstLabel;
                    return true;
                }
            }
        }

        // 3. Header.
        if (context.Request.Headers.TryGetValue(_options.HeaderName, out var headerValue))
        {
            var candidate = headerValue.ToString();
            if (IsValidSlug(candidate))
            {
                slug = candidate;
                return true;
            }
        }

        slug = null;
        return false;
    }

    private bool IsReserved(string slug)
    {
        foreach (var reserved in _options.ReservedSubdomains)
        {
            if (string.Equals(reserved, slug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsValidSlug(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return SlugPattern().IsMatch(candidate);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
