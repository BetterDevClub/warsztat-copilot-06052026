using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Identity;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Prefix).IsRequired().HasMaxLength(32);
        builder.Property(x => x.KeyHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.RevokedAt });
    }
}
