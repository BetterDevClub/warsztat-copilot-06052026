using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BookSlot.Infrastructure.Security;

/// <summary>
/// Fail-fast guard that prevents Production runs from starting with the dev
/// signing key, dev API-key pepper, or any obviously placeholder secret. Runs
/// once on startup as a hosted service so misconfiguration crashes the host
/// before it accepts traffic.
/// </summary>
public sealed class ProductionSecretsValidator : IHostedService
{
    private readonly IHostEnvironment _env;
    private readonly IOptions<JwtOptions> _jwt;

    private static readonly string[] ForbiddenMarkers =
    {
        "dev-", "change-me", "changeme", "please-change", "placeholder", "todo",
    };

    public ProductionSecretsValidator(IHostEnvironment env, IOptions<JwtOptions> jwt)
    {
        _env = env;
        _jwt = jwt;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_env.IsProduction()) return Task.CompletedTask;

        var jwt = _jwt.Value;
        EnsureNoDevMarker(nameof(jwt.SigningKey), jwt.SigningKey);
        EnsureNoDevMarker(nameof(jwt.ApiKeyPepper), jwt.ApiKeyPepper);

        if (jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"{nameof(JwtOptions)}.{nameof(jwt.SigningKey)} must be at least 32 characters in Production.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void EnsureNoDevMarker(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{nameof(JwtOptions)}.{name} is not configured.");
        }
        var lowered = value.ToLowerInvariant();
        foreach (var marker in ForbiddenMarkers)
        {
            if (lowered.Contains(marker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{nameof(JwtOptions)}.{name} contains placeholder marker '{marker}'. " +
                    "Provide a real secret via environment variable or user secrets before running in Production.");
            }
        }
    }
}
