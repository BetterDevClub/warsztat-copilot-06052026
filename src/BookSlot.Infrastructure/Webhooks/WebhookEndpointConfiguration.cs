using BookSlot.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Webhooks;

/// <summary>EF mapping for <see cref="WebhookEndpoint"/>.</summary>
internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Url).IsRequired().HasMaxLength(WebhookEndpoint.MaxUrlLength);
        builder.Property(e => e.Secret).IsRequired().HasMaxLength(WebhookEndpoint.MaxSecretLength);
        builder.Property(e => e.Description).HasMaxLength(WebhookEndpoint.MaxDescriptionLength);
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt);

        // Postgres text[] for the subscribed events collection — queryable by `ANY` / `@>` operators.
        builder.Property<IReadOnlyList<string>>("SubscribedEvents")
            .HasField("_subscribedEvents")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.IsActive });
    }
}
