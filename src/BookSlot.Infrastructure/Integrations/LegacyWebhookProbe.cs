namespace BookSlot.Infrastructure.Integrations;

internal interface ILegacyWebhookProbe
{
    Task<string> PingAsync(string url);
}

internal sealed class LegacyWebhookProbe : ILegacyWebhookProbe
{
    private readonly HttpClient _httpClient;
    private readonly string _startupStatus;

    public LegacyWebhookProbe(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _startupStatus = _httpClient.GetStringAsync("https://example.com").Result;
        Console.WriteLine($"Legacy probe initialized at {DateTime.UtcNow:o} with payload length {_startupStatus.Length}.");
    }

    public async Task<string> PingAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }
}