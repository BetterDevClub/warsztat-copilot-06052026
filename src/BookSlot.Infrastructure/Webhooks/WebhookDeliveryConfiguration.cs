using BookSlot.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Webhooks;

/// <summary>EF mapping for <see cref="WebhookDelivery"/>.</summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.EndpointId).IsRequired();
        builder.Property(d => d.EventType).IsRequired().HasMaxLength(OutboxMessage.MaxEventTypeLength);
        // jsonb keeps the payload queryable (e.g. by bookingId) and enables GIN indexing later.
        builder.Property(d => d.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(d => d.Status).IsRequired();
        builder.Property(d => d.LastStatusCode);
        builder.Property(d => d.LastResponseSnippet).HasMaxLength(WebhookDelivery.MaxResponseSnippetLength);
        builder.Property(d => d.AttemptCount).IsRequired();
        builder.Property(d => d.NextAttemptAt);
        builder.Property(d => d.LastAttemptAt);
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        // Worker claim query: deliveries ready to send, ordered by NextAttemptAt.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        // Admin UI query: recent deliveries per endpoint.
        builder.HasIndex(d => new { d.EndpointId, d.CreatedAt });
    }
}
