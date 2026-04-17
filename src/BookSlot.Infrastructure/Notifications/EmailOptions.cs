using System.ComponentModel.DataAnnotations;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>Root configuration for the email stack.</summary>
public sealed class EmailOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>Selected provider: <c>Null</c>, <c>Smtp</c>, or <c>SendGrid</c>. Case-insensitive.</summary>
    [Required]
    public string Provider { get; set; } = "Null";

    /// <summary>Default sender address for tenant-agnostic mails.</summary>
    [Required, EmailAddress]
    public string FromEmail { get; set; } = "no-reply@bookslot.local";

    /// <summary>Default sender display name.</summary>
    public string FromName { get; set; } = "BookSlot";

    /// <summary>SMTP-specific settings (used when <see cref="Provider"/> = <c>Smtp</c>).</summary>
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>SendGrid-specific settings (used when <see cref="Provider"/> = <c>SendGrid</c>).</summary>
    public SendGridSettings SendGrid { get; set; } = new();

    /// <summary>SMTP server credentials.</summary>
    public sealed class SmtpSettings
    {
        /// <summary>SMTP host.</summary>
        public string Host { get; set; } = "localhost";

        /// <summary>SMTP port.</summary>
        public int Port { get; set; } = 1025;

        /// <summary>Username for SMTP AUTH, optional.</summary>
        public string? Username { get; set; }

        /// <summary>Password for SMTP AUTH, optional.</summary>
        public string? Password { get; set; }

        /// <summary>Enable TLS / STARTTLS.</summary>
        public bool UseSsl { get; set; }
    }

    /// <summary>SendGrid credentials.</summary>
    public sealed class SendGridSettings
    {
        /// <summary>SendGrid API key.</summary>
        public string? ApiKey { get; set; }
    }
}
