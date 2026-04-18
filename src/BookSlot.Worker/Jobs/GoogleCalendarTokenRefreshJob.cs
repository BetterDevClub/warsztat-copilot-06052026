using System.Text.Json.Serialization;
using BookSlot.Domain.Integrations;
using BookSlot.Infrastructure.Integrations;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookSlot.Worker.Jobs;

/// <summary>
/// Keeps Google OAuth2 access tokens for <see cref="IntegrationConnection"/>
/// rows alive. Refresh is eager: any active connection whose access token
/// expires inside <see cref="EagerRefreshWindow"/> is POSTed to Google's
/// <c>/token</c> endpoint with <c>grant_type=refresh_token</c>. A single-point
/// circuit breaker suspends further calls for <see cref="CircuitCooldown"/>
/// after <see cref="CircuitTripAfter"/> consecutive transport errors, so a
/// sustained Google outage does not exhaust the worker thread.
/// A <c>400 invalid_grant</c> response is terminal — the connection is
/// deactivated and must be reconnected by the tenant owner.
/// </summary>
internal sealed class GoogleCalendarTokenRefreshJob : IWorkerJob
{
    private static readonly TimeSpan EagerRefreshWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CircuitCooldown = TimeSpan.FromMinutes(5);
    private const int CircuitTripAfter = 3;
    private const int BatchSize = 100;

    private static DateTimeOffset _circuitOpenUntil = DateTimeOffset.MinValue;
    private static int _consecutiveFailures;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GoogleOAuthOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GoogleCalendarTokenRefreshJob> _logger;

    public GoogleCalendarTokenRefreshJob(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GoogleOAuthOptions> options,
        TimeProvider clock,
        ILogger<GoogleCalendarTokenRefreshJob> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "google-calendar-token-refresh";

    public TimeSpan Interval => TimeSpan.FromMinutes(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            _logger.LogDebug("Google OAuth not configured — token refresh job is a no-op.");
            return;
        }

        var now = _clock.GetUtcNow();
        if (now < _circuitOpenUntil)
        {
            _logger.LogDebug("Google token refresh circuit open until {Until}; skipping tick.", _circuitOpenUntil);
            return;
        }

        var deadline = now + EagerRefreshWindow;
        var due = await _db.IntegrationConnections
            .IgnoreQueryFilters()
            .Where(c => c.IsActive
                        && c.Provider == MeetingProvider.GoogleMeet
                        && c.RefreshToken != null
                        && (c.AccessTokenExpiresAt == null || c.AccessTokenExpiresAt <= deadline))
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (due.Count == 0) return;

        using var client = _httpClientFactory.CreateClient("google-oauth");
        client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
        client.Timeout = TimeSpan.FromSeconds(15);

        foreach (var conn in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RefreshAsync(client, conn, options, cancellationToken).ConfigureAwait(false);
                _consecutiveFailures = 0;
            }
            catch (HttpRequestException ex)
            {
                _consecutiveFailures++;
                _logger.LogWarning(ex, "Google token refresh transport failure ({Consecutive}/{Trip}).",
                    _consecutiveFailures, CircuitTripAfter);
                if (_consecutiveFailures >= CircuitTripAfter)
                {
                    _circuitOpenUntil = _clock.GetUtcNow() + CircuitCooldown;
                    _logger.LogWarning("Google token refresh circuit opened until {Until}.", _circuitOpenUntil);
                    break;
                }
            }
#pragma warning disable CA1031 // individual refresh failures must not abort the batch
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Google token refresh unexpected error for connection {Connection}.", conn.Id);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshAsync(
        HttpClient client,
        IntegrationConnection connection,
        GoogleOAuthOptions options,
        CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = connection.RefreshToken!,
            ["grant_type"] = "refresh_token",
        });

        using var response = await client.PostAsync("token", form, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 400 || (int)response.StatusCode == 401)
        {
            connection.Deactivate(_clock.GetUtcNow());
            _logger.LogWarning("Google token refresh terminal error {Status} for connection {Connection} — deactivated.",
                (int)response.StatusCode, connection.Id);
            return;
        }

        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken)
            .ConfigureAwait(false);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            _logger.LogWarning("Google token refresh returned empty payload for connection {Connection}.", connection.Id);
            return;
        }

        var expiresAt = _clock.GetUtcNow().AddSeconds(Math.Max(60, token.ExpiresIn));
        connection.UpdateTokens(token.AccessToken, token.RefreshToken, expiresAt, _clock.GetUtcNow());
        _logger.LogInformation("Refreshed Google access token for connection {Connection} (expires {Expires}).",
            connection.Id, expiresAt);
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("scope")] string? Scope);
}
