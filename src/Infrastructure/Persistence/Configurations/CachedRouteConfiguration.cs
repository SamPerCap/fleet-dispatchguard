using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class CachedRouteConfiguration : EntityConfiguration<CachedRoute>
{
    public override void Configure(EntityTypeBuilder<CachedRoute> builder)
    {
        base.Configure(builder);

        builder.ToTable("CachedRoutes");

        builder.HasOne(cr => cr.OriginLocation)
            .WithMany()
            .HasForeignKey(cr => cr.OriginLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cr => cr.DestinationLocation)
            .WithMany()
            .HasForeignKey(cr => cr.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(cr => cr.IntermediateLocationIdsJson)
            .HasMaxLength(1000)
            .HasDefaultValue("[]");

        builder.Property(cr => cr.ServiceProvider)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("GoogleMaps");

        builder.Property(cr => cr.RawServiceResponse)
            .HasMaxLength(4000);

        builder.Property(cr => cr.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.OwnsOne(cr => cr.Metrics, metrics =>
        {
            metrics.Property(m => m.DistanceKm)
                .HasColumnName("DistanceKm")
                .HasPrecision(10, 2)
                .IsRequired();

            metrics.Property(m => m.EstimatedDuration)
                .HasColumnName("EstimatedDuration")
                .IsRequired();

            metrics.Property(m => m.CalculatedAt)
                .HasColumnName("MetricsCalculatedAt")
                .IsRequired();
        });

        builder.HasIndex(cr => new { cr.OriginLocationId, cr.DestinationLocationId });
        builder.HasIndex(cr => cr.ServiceProvider);
    }
}