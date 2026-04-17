using Microsoft.Extensions.Logging;

namespace BookSlot.Features.Shared.Emailing;

/// <summary>
/// Development-only <see cref="IEmailSender"/> that writes the message to the logger.
/// Reset/confirm tokens are printed verbatim so developers can click the link in dev
/// without a running SMTP relay. Phase 15 replaces this with real adapters.
/// </summary>
public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    /// <summary>Creates a new sender.</summary>
    public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogInformation("[DEV EMAIL] To={To} Subject={Subject}\n{Body}",
            message.To, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }
}
