using BookSlot.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>Dev-default SMS sender — logs the payload. Used when <c>Sms:Provider</c> is <c>Null</c>.</summary>
internal sealed class NullSmsSender : ISmsSender
{
    private readonly ILogger<NullSmsSender> _logger;

    /// <summary>Creates a new instance.</summary>
    public NullSmsSender(ILogger<NullSmsSender> logger) => _logger = logger;

    /// <inheritdoc />
    public Task<string?> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogInformation(
            "[NullSmsSender] to={To} bodyLength={Length}",
            message.To, message.Body?.Length ?? 0);
        return Task.FromResult<string?>("null://" + Guid.NewGuid().ToString("N"));
    }
}
