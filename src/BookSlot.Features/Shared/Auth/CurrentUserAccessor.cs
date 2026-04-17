using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BookSlot.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace BookSlot.Features.Shared.Auth;

/// <summary>
/// Scoped <see cref="ICurrentUser"/> implementation that reads the authenticated
/// principal from <see cref="HttpContext.User"/>. Safe to resolve in any ASP.NET
/// Core request scope — in tests use the fake variants from the integration test
/// fixtures instead.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates a new accessor. <paramref name="httpContextAccessor"/> must be registered in DI.</summary>
    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email)
        ?? Principal?.FindFirstValue(ClaimTypes.Email);

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => Principal is null
        ? []
        : Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    /// <inheritdoc />
    public bool IsInRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return Principal?.IsInRole(role) ?? false;
    }
}
