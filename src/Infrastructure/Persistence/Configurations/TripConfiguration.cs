using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class TripConfiguration : EntityConfiguration<Trip>
{
    /// <summary>Nobody reads them back: they stay worded as the rows that already exist (SPEC 0095).</summary>
    private static readonly ValueConverter<IReadOnlyCollection<LocalizedText>, string[]> Worded = new(
        texts => texts.Select(text => text.In(Language.Spanish)).ToArray(),
        stored => stored.Select(Texts.AsWritten).ToList());

    private static readonly ValueComparer<IReadOnlyCollection<LocalizedText>> SameTexts = new(
        (one, other) => one == other || (one != null && other != null && one.SequenceEqual(other)),
        texts => texts.Aggregate(0, (hash, text) => HashCode.Combine(hash, text.GetHashCode())),
        texts => texts.ToList());

    public override void Configure(EntityTypeBuilder<Trip> builder)
    {
        base.Configure(builder);

        // The rows that existed before bases went to the default one (SPEC 0050).
        builder.Property(x => x.BaseId).IsRequired().HasDefaultValue(OperatingBase.DefaultId);
        builder.HasOne<OperatingBase>().WithMany().HasForeignKey(x => x.BaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BaseId);
        builder.ToTable("Trips");

        builder.HasOne(t => t.OriginLocation)
            .WithMany()
            .HasForeignKey(t => t.OriginLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DestinationLocation)
            .WithMany()
            .HasForeignKey(t => t.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.AssignedDriver)
            .WithMany()
            .HasForeignKey(t => t.AssignedDriverId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(t => t.AssignedVehicle)
            .WithMany()
            .HasForeignKey(t => t.AssignedVehicleId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne(t => t.Booking)
            .WithMany(b => b.Trips)
            .HasForeignKey(t => t.BookingId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(t => t.Circuit)
            .WithMany(c => c.Trips)
            .HasForeignKey(t => t.CircuitId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasMany(t => t.IntermediateStops)
            .WithOne(ts => ts.Trip)
            .HasForeignKey(ts => ts.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.RequiredSeats)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(Trip.MaxDescriptionLength);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(t => t.ServiceType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(t => t.IsLocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.LastOptimizationDate);

            builder.OwnsOne(t => t.ScheduledWindow, swBuilder =>
            {
                swBuilder.Property(tw => tw.Start)
                    .HasColumnName("ScheduledWindowStart")
                    .IsRequired();

                swBuilder.Property(tw => tw.End)
                    .HasColumnName("ScheduledWindowEnd")
                    .IsRequired();
            });

            builder.OwnsOne(t => t.CalculatedMetrics, cmBuilder =>
            {
                cmBuilder.Property(m => m.DistanceKm)
                    .HasColumnName("CalculatedDistanceInKm")
                    .HasPrecision(10, 2);

                cmBuilder.Property(m => m.EstimatedDuration)
                    .HasColumnName("CalculatedEstimatedDurationInMinutes");

                cmBuilder.Property(m => m.CalculatedAt)
                    .HasColumnName("CalculatedMetricsCalculatedAt");
            });

            builder.Property(t => t.ForcedReason).HasMaxLength(AllocationOverride.MaxReasonLength);
            builder.Property(t => t.ForcedBy).HasMaxLength(AllocationOverride.MaxByLength);
            builder.Ignore(t => t.IsForced);
            builder.Ignore(t => t.ForcedDescription);

            // A JSON column, always loaded with the trip (SPEC 0063 R8).
            builder.OwnsMany(t => t.ForcedByTrips, f =>
            {
                f.ToJson("ForcedByTrips");
                f.Property(x => x.Reason).IsRequired();
                f.Property(x => x.By).IsRequired();
            });
            builder.Navigation(t => t.ForcedByTrips).UsePropertyAccessMode(PropertyAccessMode.Field);

            builder.OwnsOne(t => t.LegalValidation, lvBuilder =>
            {
                lvBuilder.Property(lv => lv.IsCompliant)
                    .HasColumnName("LegalValidationIsCompliant");

                lvBuilder.Property(lv => lv.Violations)
                    .HasColumnName("LegalValidationViolations") // No limit: one per later service and rule (SPEC 0063).
                    .HasConversion(Worded, SameTexts)
                    .IsRequired(false);

                lvBuilder.Property(lv => lv.Warnings)
                    .HasColumnName("LegalValidationWarnings")
                    .HasConversion(Worded, SameTexts)
                    .IsRequired(false);

                lvBuilder.Property(lv => lv.ValidatedAt)
                    .HasColumnName("LegalValidationValidatedAt");
            });

            builder.HasIndex(t => t.Status);
            builder.HasIndex(t => t.AssignedDriverId);
            builder.HasIndex(t => t.AssignedVehicleId);
            builder.HasIndex(t => t.CircuitId);
            builder.HasIndex(t => t.IsLocked);
            builder.HasIndex(t => new { t.OriginLocationId, t.DestinationLocationId });
    }
}