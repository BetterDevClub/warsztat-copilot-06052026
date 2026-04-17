using BookSlot.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Tenants;

/// <summary>EF mapping for <see cref="TenantSettings"/>.</summary>
internal sealed class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.Property(s => s.TimeZoneId).HasMaxLength(80).IsRequired();
        builder.Property(s => s.ContactEmail).HasMaxLength(256);
        builder.Property(s => s.BrandingPrimaryColor).HasMaxLength(16);
        builder.Property(s => s.BrandingLogoUrl).HasMaxLength(1024);
        builder.Property(s => s.BookingWindowDays).IsRequired();
        builder.Property(s => s.UpdatedAt);
    }
}
