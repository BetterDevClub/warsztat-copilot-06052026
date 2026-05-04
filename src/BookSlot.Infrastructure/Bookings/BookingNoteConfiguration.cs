using BookSlot.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Bookings;

/// <summary>EF mapping for <see cref="BookingNote"/>.</summary>
internal sealed class BookingNoteConfiguration : IEntityTypeConfiguration<BookingNote>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BookingNote> builder)
    {
        builder.ToTable("booking_notes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.BookingId).IsRequired();
        builder.Property(n => n.AuthorId).IsRequired();
        builder.Property(n => n.Content).HasMaxLength(BookingNote.MaxContentLength).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();

        // FK to bookings with RESTRICT delete: booking cannot be deleted while notes exist.
        builder.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(n => n.BookingId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hot query: list/count notes for a booking in chronological order.
        builder.HasIndex(n => new { n.TenantId, n.BookingId, n.CreatedAt });
    }
}
