using BookSlot.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Integrations;

/// <summary>EF mapping for <see cref="IntegrationConnection"/>.</summary>
internal sealed class IntegrationConnectionConfiguration : IEntityTypeConfiguration<IntegrationConnection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IntegrationConnection> builder)
    {
        builder.ToTable("integration_connections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Provider).IsRequired();
        builder.Property(c => c.StaffId);
        builder.Property(c => c.ExternalAccountId).IsRequired().HasMaxLength(IntegrationConnection.MaxAccountLength);
        builder.Property(c => c.AccessToken).HasMaxLength(IntegrationConnection.MaxTokenLength);
        builder.Property(c => c.RefreshToken).HasMaxLength(IntegrationConnection.MaxTokenLength);
        builder.Property(c => c.AccessTokenExpiresAt);
        builder.Property(c => c.Scope).HasMaxLength(IntegrationConnection.MaxScopeLength);
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt);

        // One active connection per (tenant, provider, staff-or-null) — partial filter lets
        // historical/inactive rows coexist with a fresh re-connection.
        builder.HasIndex(c => new { c.TenantId, c.Provider, c.StaffId })
            .IsUnique()
            .HasFilter("is_active = true");
    }
}
