using BookSlot.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Staff;

/// <summary>EF mapping for <see cref="StaffMember"/>.</summary>
internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("staff");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(StaffMember.MaxDisplayNameLength).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(StaffMember.MaxTitleLength);
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.AvatarUrl).HasMaxLength(1024);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);
        builder.Property(s => s.IsActive).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.IsActive });
    }
}

/// <summary>EF mapping for <see cref="StaffServiceAssignment"/>.</summary>
internal sealed class StaffServiceAssignmentConfiguration : IEntityTypeConfiguration<StaffServiceAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StaffServiceAssignment> builder)
    {
        builder.ToTable("staff_service_assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.StaffId).IsRequired();
        builder.Property(a => a.ServiceTypeId).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.StaffId, a.ServiceTypeId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.ServiceTypeId });
    }
}

/// <summary>EF mapping for <see cref="AvailabilityRule"/>.</summary>
internal sealed class AvailabilityRuleConfiguration : IEntityTypeConfiguration<AvailabilityRule>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AvailabilityRule> builder)
    {
        builder.ToTable("availability_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.StaffId).IsRequired();
        builder.Property(r => r.DayOfWeek).IsRequired();
        builder.Property(r => r.StartTime).IsRequired();
        builder.Property(r => r.EndTime).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.StaffId, r.DayOfWeek });
    }
}

/// <summary>EF mapping for <see cref="AvailabilityOverride"/>.</summary>
internal sealed class AvailabilityOverrideConfiguration : IEntityTypeConfiguration<AvailabilityOverride>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AvailabilityOverride> builder)
    {
        builder.ToTable("availability_overrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TenantId).IsRequired();
        builder.Property(o => o.StaffId).IsRequired();
        builder.Property(o => o.Date).IsRequired();
        builder.Property(o => o.IsUnavailable).IsRequired();
        builder.Property(o => o.StartTime);
        builder.Property(o => o.EndTime);
        builder.Property(o => o.Reason).HasMaxLength(AvailabilityOverride.MaxReasonLength);

        builder.HasIndex(o => new { o.TenantId, o.StaffId, o.Date });
    }
}
