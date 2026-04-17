using BookSlot.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Bookings;

/// <summary>EF mapping for <see cref="RecurringBooking"/>.</summary>
internal sealed class RecurringBookingConfiguration : IEntityTypeConfiguration<RecurringBooking>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecurringBooking> builder)
    {
        builder.ToTable("recurring_bookings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.StaffId).IsRequired();
        builder.Property(r => r.ServiceTypeId).IsRequired();
        builder.Property(r => r.IntervalWeeks).IsRequired();
        builder.Property(r => r.DayOfWeek).IsRequired();
        builder.Property(r => r.LocalStartTime).IsRequired();
        builder.Property(r => r.StartDate).IsRequired();
        builder.Property(r => r.EndDate);
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.GuestName).HasMaxLength(Booking.MaxGuestNameLength).IsRequired();
        builder.Property(r => r.GuestEmail).HasMaxLength(Booking.MaxGuestEmailLength).IsRequired();
        builder.Property(r => r.GuestPhone).HasMaxLength(Booking.MaxGuestPhoneLength);
        builder.Property(r => r.GuestNotes).HasMaxLength(Booking.MaxGuestNotesLength);
        builder.Property(r => r.LastGeneratedThrough);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt);

        // Hot query: the worker scans active templates per tenant.
        builder.HasIndex(r => new { r.TenantId, r.Status });
        builder.HasIndex(r => new { r.StaffId, r.Status });
    }
}
