using BookSlot.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Notifications;

/// <summary>EF mapping for <see cref="NotificationLog"/>.</summary>
internal sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.Kind).IsRequired();
        builder.Property(n => n.Channel).IsRequired();
        builder.Property(n => n.Recipient).IsRequired().HasMaxLength(NotificationLog.MaxRecipientLength);
        builder.Property(n => n.Subject).HasMaxLength(NotificationLog.MaxSubjectLength);
        builder.Property(n => n.DedupKey).IsRequired().HasMaxLength(NotificationLog.MaxDedupKeyLength);
        builder.Property(n => n.Status).IsRequired();
        builder.Property(n => n.ProviderMessageId).HasMaxLength(200);
        builder.Property(n => n.Error).HasMaxLength(NotificationLog.MaxErrorLength);
        builder.Property(n => n.AttemptCount).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.LastAttemptAt);
        builder.Property(n => n.SentAt);

        // Dedup guarantee — one logical send per tenant.
        builder.HasIndex(n => new { n.TenantId, n.DedupKey }).IsUnique();
        // Operational query: inspect recent activity per tenant.
        builder.HasIndex(n => new { n.TenantId, n.CreatedAt });
    }
}
