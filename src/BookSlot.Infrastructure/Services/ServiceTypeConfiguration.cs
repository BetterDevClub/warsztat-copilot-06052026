using BookSlot.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Services;

/// <summary>EF mapping for <see cref="ServiceType"/>.</summary>
internal sealed class ServiceTypeConfiguration : IEntityTypeConfiguration<ServiceType>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable("service_types");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(ServiceType.MaxNameLength).IsRequired();
        builder.Property(s => s.Slug).HasMaxLength(64).IsRequired();
        builder.Property(s => s.DurationMinutes).IsRequired();
        builder.Property(s => s.BufferBeforeMinutes).IsRequired();
        builder.Property(s => s.BufferAfterMinutes).IsRequired();
        builder.Property(s => s.Price).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(s => s.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(s => s.Description).HasMaxLength(ServiceType.MaxDescriptionLength);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.FormSchemaJson).HasColumnType("jsonb");

        // Slugs are unique per tenant, not globally.
        builder.HasIndex(s => new { s.TenantId, s.Slug }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.IsActive });
    }
}
