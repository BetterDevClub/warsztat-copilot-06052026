namespace BookSlot.Domain.Integrations;

/// <summary>
/// Abstraction over the meeting-link provider (Zoom / Google Meet / etc.).
/// </summary>
public interface IMeetingLinkGenerator
{
    /// <summary>Which provider this generator speaks to.</summary>
    MeetingProvider Provider { get; }

    /// <summary>Creates a meeting for a booking window.</summary>
    Task<MeetingLink> CreateMeetingAsync(
        string topic,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default);
}
