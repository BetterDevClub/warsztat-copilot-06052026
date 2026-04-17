using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookSlot.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookSlot.Infrastructure.Security;

/// <summary>HS256 access token issuer. Reads signing material from <see cref="JwtOptions"/>.</summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    /// <summary>Claim carrying the tenant slug — consumed by the tenant resolution middleware.</summary>
    public const string TenantSlugClaim = "tenant_slug";

    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates a new generator. <paramref name="clock"/> is used for <c>iat</c>/<c>exp</c>.</summary>
    public JwtTokenGenerator(IOptions<JwtOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        _options = options.Value;
        _clock = clock;
    }

    /// <inheritdoc />
    public AccessToken CreateAccessToken(ApplicationUser user, string tenantSlug, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(roles);

        var now = _clock.GetUtcNow();
        var expires = now.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(TenantSlugClaim, tenantSlug),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var value = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new AccessToken(value, expires);
    }
}
