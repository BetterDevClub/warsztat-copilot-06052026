using System.Globalization;
using BookSlot.Domain.Integrations;

namespace BookSlot.Infrastructure.Integrations;

/// <summary>
/// Deterministic Zoom mock used until the real Zoom HTTP adapter lands in Phase 22.
/// Returns a plausible-looking join URL and a 6-digit passcode so downstream code
/// (templates, notifications, iCal generation) can be developed end-to-end.
/// </summary>
internal sealed class MockZoomMeetingLinkGenerator : IMeetingLinkGenerator
{
    /// <inheritdoc />
    public MeetingProvider Provider => MeetingProvider.Zoom;

    /// <inheritdoc />
    public Task<MeetingLink> CreateMeetingAsync(
        string topic,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        _ = topic;
        _ = endUtc;
        _ = startUtc;

        var rand = Random.Shared;
        var id = ((long)rand.Next(100_000, 999_999) * 10_000L) + rand.Next(0, 9999);
        var meetingId = id.ToString(CultureInfo.InvariantCulture);
        var passcode = rand.Next(0, 999_999).ToString("D6", CultureInfo.InvariantCulture);
        var url = $"https://zoom.us/j/{meetingId}?pwd={passcode}";

        return Task.FromResult(new MeetingLink(MeetingProvider.Zoom, url, passcode, meetingId));
    }
}
