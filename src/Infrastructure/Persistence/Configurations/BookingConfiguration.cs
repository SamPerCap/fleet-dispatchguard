using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

/// <summary>The assignment state is derived, not a column (SPEC 0045 R2).</summary>
public class BookingConfiguration : EntityConfiguration<Booking>
{
    public override void Configure(EntityTypeBuilder<Booking> builder)
    {
        base.Configure(builder);
        builder.ToTable("Bookings");

        builder.Property(b => b.CustomerName)
            .IsRequired()
            .HasMaxLength(Booking.CustomerNameMaxLength);

        builder.Property(b => b.Description)
            .IsRequired()
            .HasMaxLength(Booking.DescriptionMaxLength);

        builder.Property(b => b.Notes)
            .HasMaxLength(Booking.NotesMaxLength);

        builder.Ignore(b => b.AssignmentStatus);

        builder.Navigation(b => b.Trips)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(b => b.CustomerName);
    }
}
