namespace BookSlot.Domain.Integrations;

/// <summary>
/// Provider-agnostic meeting link produced by an <see cref="IMeetingLinkGenerator"/>.
/// </summary>
public sealed record MeetingLink(
    MeetingProvider Provider,
    string JoinUrl,
    string? Passcode,
    string ExternalMeetingId);
