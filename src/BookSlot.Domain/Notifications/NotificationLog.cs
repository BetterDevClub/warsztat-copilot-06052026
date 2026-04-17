using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Notifications;

/// <summary>
/// Audit entry for a single notification dispatch attempt. Uniquely keyed by
/// <c>(TenantId, DedupKey)</c> so that retries of the same logical event do not
/// cause duplicate delivery — callers supply a stable <see cref="DedupKey"/>
/// such as <c>booking:{id}:confirmed</c>.
/// </summary>
public sealed class NotificationLog : Entity<Guid>, ITenantScoped
{
    /// <summary>Maximum length of the dedup key (must fit a b-tree index).</summary>
    public const int MaxDedupKeyLength = 200;

    /// <summary>Maximum length of the recipient field.</summary>
    public const int MaxRecipientLength = 320;

    /// <summary>Maximum length of the subject field.</summary>
    public const int MaxSubjectLength = 300;

    /// <summary>Maximum length of the stored error message.</summary>
    public const int MaxErrorLength = 2000;

    private NotificationLog() { }

    private NotificationLog(
        Guid id,
        Guid tenantId,
        NotificationKind kind,
        NotificationChannel channel,
        string recipient,
        string? subject,
        string dedupKey,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        Channel = channel;
        Recipient = recipient;
        Subject = subject;
        DedupKey = dedupKey;
        Status = NotificationStatus.Pending;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Business reason for this notification.</summary>
    public NotificationKind Kind { get; private set; }

    /// <summary>Delivery medium.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>Normalised recipient address (email or E.164 phone).</summary>
    public string Recipient { get; private set; } = default!;

    /// <summary>Email subject line (null for SMS).</summary>
    public string? Subject { get; private set; }

    /// <summary>Stable deduplication key — composite unique with <see cref="TenantId"/>.</summary>
    public string DedupKey { get; private set; } = default!;

    /// <summary>Current lifecycle state.</summary>
    public NotificationStatus Status { get; private set; }

    /// <summary>Provider-assigned message id, when available.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>Last error message on failure, truncated.</summary>
    public string? Error { get; private set; }

    /// <summary>Number of delivery attempts so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>UTC timestamp of the last attempt, if any.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>UTC timestamp of successful delivery, if any.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    // -------------------------------------------------------------------------

    /// <summary>Records a successful dispatch.</summary>
    public void MarkSent(string? providerMessageId, DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        ProviderMessageId = providerMessageId;
        Error = null;
        SentAt = now;
        LastAttemptAt = now;
        AttemptCount++;
    }

    /// <summary>Records a failed dispatch attempt.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = NotificationStatus.Failed;
        Error = Truncate(error, MaxErrorLength);
        LastAttemptAt = now;
        AttemptCount++;
    }

    /// <summary>Marks the entry as suppressed (never dispatched).</summary>
    public void MarkSuppressed(string reason, DateTimeOffset now)
    {
        Status = NotificationStatus.Suppressed;
        Error = Truncate(reason, MaxErrorLength);
        LastAttemptAt = now;
    }

    // -------------------------------------------------------------------------

    /// <summary>Creates a new pending log entry.</summary>
    public static Result<NotificationLog> Create(
        Guid id,
        Guid tenantId,
        NotificationKind kind,
        NotificationChannel channel,
        string recipient,
        string? subject,
        string dedupKey,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(recipient) || recipient.Length > MaxRecipientLength)
            return Result.Failure<NotificationLog>(Primitives.Error.Validation("NotificationLog.RecipientInvalid",
                $"Recipient is required and must be {MaxRecipientLength} characters or fewer."));

        if (string.IsNullOrWhiteSpace(dedupKey) || dedupKey.Length > MaxDedupKeyLength)
            return Result.Failure<NotificationLog>(Primitives.Error.Validation("NotificationLog.DedupKeyInvalid",
                $"Dedup key is required and must be {MaxDedupKeyLength} characters or fewer."));

        if (subject is not null && subject.Length > MaxSubjectLength)
            return Result.Failure<NotificationLog>(Primitives.Error.Validation("NotificationLog.SubjectTooLong",
                $"Subject must be {MaxSubjectLength} characters or fewer."));

        return new NotificationLog(id, tenantId, kind, channel, recipient.Trim(), subject?.Trim(), dedupKey.Trim(), now);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
