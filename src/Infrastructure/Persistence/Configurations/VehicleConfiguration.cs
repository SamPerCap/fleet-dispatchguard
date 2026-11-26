using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class VehicleConfiguration : EntityConfiguration<Vehicle>
{
    public override void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        base.Configure(builder);

        // The rows that existed before bases went to the default one (SPEC 0050).
        builder.Property(x => x.BaseId).IsRequired().HasDefaultValue(OperatingBase.DefaultId);
        builder.HasOne<OperatingBase>().WithMany().HasForeignKey(x => x.BaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BaseId);
        builder.ToTable("Vehicles");

        builder.Property(v => v.LicensePlate)
            .IsRequired()
            .HasMaxLength(Vehicle.MaxLicensePlateLength);

        builder.Property(v => v.Brand)
            .IsRequired()
            .HasMaxLength(Vehicle.MaxBrandOrModelLength);

        builder.Property(v => v.Model)
            .IsRequired()
            .HasMaxLength(Vehicle.MaxBrandOrModelLength);

        builder.Property(v => v.SeatingCapacity)
            .IsRequired();

        builder.Property(v => v.OdometerKm)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(v => v.RegistrationDate);

        builder.Property(v => v.IsExternal)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(v => v.AllowsAutoAssignment)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(v => v.DecommissionedDate);

        builder.HasOne(v => v.HomeLocation)
            .WithMany()
            .HasForeignKey(v => v.HomeLocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasMany(v => v.Unavailabilities)
            .WithOne(vu => vu.Vehicle)
            .HasForeignKey(vu => vu.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => v.LicensePlate).IsUnique();
        builder.HasIndex(v => v.Brand);
        builder.HasIndex(v => v.IsExternal);
        builder.HasIndex(v => v.AllowsAutoAssignment);
        builder.HasIndex(v => v.DecommissionedDate);
    }
}