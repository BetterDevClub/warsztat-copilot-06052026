namespace BookSlot.Domain.Integrations;

/// <summary>Meeting / conferencing provider supported by the platform.</summary>
public enum MeetingProvider
{
    /// <summary>Zoom meetings.</summary>
    Zoom = 1,

    /// <summary>Google Meet (created alongside Google Calendar events).</summary>
    GoogleMeet = 2,
}
