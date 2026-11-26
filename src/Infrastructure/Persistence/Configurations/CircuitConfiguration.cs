using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class CircuitConfiguration : EntityConfiguration<Circuit>
{
    public override void Configure(EntityTypeBuilder<Circuit> builder)
    {
        base.Configure(builder);
        builder.ToTable("Circuits");

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.PreferSameDriver)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(c => c.PreferSameVehicle)
            .IsRequired()
            .HasDefaultValue(true);

        builder.OwnsOne(c => c.Duration, dBuilder =>
        {
            dBuilder.Property(tw => tw.Start)
                .HasColumnName("DurationStart")
                .IsRequired();

            dBuilder.Property(tw => tw.End)
                .HasColumnName("DurationEnd")
                .IsRequired();
        });

        builder.HasMany(c => c.Trips)
            .WithOne(t => t.Circuit)
            .HasForeignKey(t => t.CircuitId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.PreferSameDriver);
        builder.HasIndex(c => c.PreferSameVehicle);
    }
}