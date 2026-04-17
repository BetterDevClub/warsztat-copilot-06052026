using System.ComponentModel.DataAnnotations;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>Root configuration for the SMS stack.</summary>
public sealed class SmsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Sms";

    /// <summary>Selected provider: <c>Null</c> or <c>Twilio</c>. Case-insensitive.</summary>
    [Required]
    public string Provider { get; set; } = "Null";

    /// <summary>Default outbound sender number in E.164.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Twilio-specific settings.</summary>
    public TwilioSettings Twilio { get; set; } = new();

    /// <summary>Twilio credentials.</summary>
    public sealed class TwilioSettings
    {
        /// <summary>Twilio account SID.</summary>
        public string? AccountSid { get; set; }

        /// <summary>Twilio auth token.</summary>
        public string? AuthToken { get; set; }
    }
}
