using BookSlot.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Webhooks;

/// <summary>EF mapping for <see cref="OutboxMessage"/>.</summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.TenantId);
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(OutboxMessage.MaxEventTypeLength);
        builder.Property(m => m.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.ProcessedAt);
        builder.Property(m => m.AttemptCount).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Worker fan-out query: unprocessed messages ordered by OccurredAt.
        builder.HasIndex(m => new { m.ProcessedAt, m.OccurredAt });
    }
}
