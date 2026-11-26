using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.ToTable("TripStops");
        builder.HasKey(ts => ts.Id);

        builder.Property(ts => ts.Id)
            .ValueGeneratedOnAdd();

        builder.HasOne(ts => ts.Trip)
            .WithMany(t => t.IntermediateStops)
            .HasForeignKey(ts => ts.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ts => ts.Location)
            .WithMany()
            .HasForeignKey(ts => ts.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(ts => ts.SequenceOrder)
            .IsRequired();

        builder.OwnsOne(ts => ts.StopWindow, swBuilder =>
        {
            swBuilder.Property(tw => tw.Start)
                .HasColumnName("StopWindowStart")
                .IsRequired();

            swBuilder.Property(tw => tw.End)
                .HasColumnName("StopWindowEnd")
                .IsRequired();
        });

        // Configured here because TripStop does not inherit from Entity.
        builder.OwnsOne(ts => ts.Audit, auditBuilder =>
        {
            auditBuilder.Property(a => a.CreatedAt)
                .HasColumnName("CreatedAt")
                .IsRequired();

            auditBuilder.Property(a => a.UpdatedAt)
                .HasColumnName("UpdatedAt")
                .IsRequired(false);
        });

        builder.HasIndex(ts => ts.TripId);
        builder.HasIndex(ts => ts.LocationId);
        builder.HasIndex(ts => ts.SequenceOrder);
        builder.HasIndex(ts => new { ts.TripId, ts.SequenceOrder }).IsUnique();
    }
}