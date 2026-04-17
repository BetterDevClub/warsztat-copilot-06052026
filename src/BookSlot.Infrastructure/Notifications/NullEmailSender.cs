using BookSlot.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>Dev-default email sender — logs the payload and returns a synthetic message id.
/// No external calls are made. Used when <c>Email:Provider</c> is <c>Null</c>.</summary>
internal sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    /// <summary>Creates a new instance.</summary>
    public NullEmailSender(ILogger<NullEmailSender> logger) => _logger = logger;

    /// <inheritdoc />
    public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogInformation(
            "[NullEmailSender] to={To} subject={Subject} textLength={Length}",
            message.To, message.Subject, message.TextBody?.Length ?? 0);
        return Task.FromResult<string?>("null://" + Guid.NewGuid().ToString("N"));
    }
}
