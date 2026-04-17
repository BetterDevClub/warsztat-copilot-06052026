using BookSlot.Domain.Notifications;
using BookSlot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>
/// Default <see cref="INotificationDispatcher"/> implementation.
/// Idempotency contract: for a given <c>(TenantId, DedupKey)</c> the dispatcher
/// inserts at most one <see cref="NotificationLog"/> row. If a prior entry exists
/// and is already Sent or Suppressed, the call is a no-op. Pending or Failed
/// entries are retried in-place (incrementing the attempt counter).
/// </summary>
internal sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly INotificationTemplateRenderer _renderer;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationDispatcher> _logger;

    /// <summary>Creates a new dispatcher.</summary>
    public NotificationDispatcher(
        AppDbContext db,
        IEmailSender emailSender,
        ISmsSender smsSender,
        INotificationTemplateRenderer renderer,
        TimeProvider clock,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _renderer = renderer;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationDispatchResult> DispatchAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();

        // IgnoreQueryFilters: dispatcher may be invoked from worker contexts without a resolved tenant.
        var existing = await _db.NotificationLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.TenantId == request.TenantId && n.DedupKey == request.DedupKey, cancellationToken)
            .ConfigureAwait(false);

        NotificationLog log;
        bool duplicate = false;

        if (existing is not null)
        {
            if (existing.Status is NotificationStatus.Sent or NotificationStatus.Suppressed)
            {
                return new NotificationDispatchResult(existing.Id, existing.Status, Duplicate: true);
            }

            log = existing;
            duplicate = true;
        }
        else
        {
            string? subject = null;
            if (request.Channel == NotificationChannel.Email)
            {
                subject = _renderer.RenderEmail(request.Kind, request.TemplateContext)?.Subject;
            }

            var creation = NotificationLog.Create(
                Guid.NewGuid(), request.TenantId, request.Kind, request.Channel,
                request.Recipient, subject, request.DedupKey, now);
            if (creation.IsFailure)
            {
                _logger.LogWarning("Notification log creation failed: {Error}", creation.Error.Code);
                return new NotificationDispatchResult(Guid.Empty, NotificationStatus.Failed, Duplicate: false);
            }

            log = creation.Value;
            _db.NotificationLogs.Add(log);
        }

        try
        {
            switch (request.Channel)
            {
                case NotificationChannel.Email:
                {
                    var content = _renderer.RenderEmail(request.Kind, request.TemplateContext);
                    if (content is null)
                    {
                        log.MarkSuppressed("No email template for kind " + request.Kind, now);
                        break;
                    }
                    var messageId = await _emailSender.SendAsync(
                        new EmailMessage(log.Recipient, content.Subject, content.HtmlBody, content.TextBody),
                        cancellationToken).ConfigureAwait(false);
                    log.MarkSent(messageId, now);
                    break;
                }
                case NotificationChannel.Sms:
                {
                    var body = _renderer.RenderSms(request.Kind, request.TemplateContext);
                    if (string.IsNullOrEmpty(body))
                    {
                        log.MarkSuppressed("No SMS template for kind " + request.Kind, now);
                        break;
                    }
                    var messageId = await _smsSender.SendAsync(
                        new SmsMessage(log.Recipient, body), cancellationToken).ConfigureAwait(false);
                    log.MarkSent(messageId, now);
                    break;
                }
                default:
                    log.MarkSuppressed("Unsupported channel " + request.Channel, now);
                    break;
            }
        }
#pragma warning disable CA1031 // transport failures must not propagate; logged and persisted for retry
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Notification dispatch failed for tenant={Tenant} kind={Kind}",
                request.TenantId, request.Kind);
            log.MarkFailed(ex.Message, now);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new NotificationDispatchResult(log.Id, log.Status, duplicate);
    }
}
