using BookSlot.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookSlot.Infrastructure.Reservations;

/// <summary>EF mapping for <see cref="SlotReservation"/>.</summary>
internal sealed class SlotReservationConfiguration : IEntityTypeConfiguration<SlotReservation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SlotReservation> builder)
    {
        builder.ToTable("slot_reservations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.StaffId).IsRequired();
        builder.Property(r => r.ServiceTypeId).IsRequired();
        builder.Property(r => r.StartUtc).IsRequired();
        builder.Property(r => r.EndUtc).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();
        builder.Property(r => r.GuestToken).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        // Used by the availability check: find active reservations that overlap a candidate slot.
        builder.HasIndex(r => new { r.TenantId, r.StaffId, r.ExpiresAtUtc });

        // Used to release a reservation by token (guest token must be unique globally).
        builder.HasIndex(r => r.GuestToken).IsUnique();
    }
}
