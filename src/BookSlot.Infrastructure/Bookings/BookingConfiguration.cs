using BookSlot.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Bookings;

/// <summary>EF mapping for <see cref="Booking"/>.</summary>
internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.StaffId).IsRequired();
        builder.Property(b => b.ServiceTypeId).IsRequired();
        builder.Property(b => b.StartUtc).IsRequired();
        builder.Property(b => b.EndUtc).IsRequired();
        builder.Property(b => b.Status).IsRequired();
        builder.Property(b => b.GuestName).HasMaxLength(Booking.MaxGuestNameLength).IsRequired();
        builder.Property(b => b.GuestEmail).HasMaxLength(Booking.MaxGuestEmailLength).IsRequired();
        builder.Property(b => b.GuestPhone).HasMaxLength(Booking.MaxGuestPhoneLength);
        builder.Property(b => b.GuestNotes).HasMaxLength(Booking.MaxGuestNotesLength);
        builder.Property(b => b.InternalNotes).HasMaxLength(Booking.MaxInternalNotesLength);
        builder.Property(b => b.RescheduledFromId);
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.UpdatedAt);

        // CancelToken and RescheduleToken must be globally unique (guests access by token alone).
        builder.HasIndex(b => b.CancelToken).IsUnique();
        builder.HasIndex(b => b.RescheduleToken).IsUnique();

        // Optimistic concurrency via PostgreSQL system column xmin (maps to the Xmin property).
        builder.Property(b => b.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Hot query: find active bookings for a staff member in a time range.
        builder.HasIndex(b => new { b.TenantId, b.StaffId, b.StartUtc, b.Status });
    }
}
