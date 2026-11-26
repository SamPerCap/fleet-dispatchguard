using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : EntityConfiguration<Location>
{
    public override void Configure(EntityTypeBuilder<Location> builder)
    {
        base.Configure(builder);

        builder.ToTable("Locations");

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(Location.MaxNameLength);

        builder.Property(l => l.Address)
            .IsRequired()
            .HasMaxLength(Location.MaxAddressLength);

        builder.Property(l => l.City)
            .IsRequired()
            .HasMaxLength(Location.MaxCityOrProvinceLength);

        builder.Property(l => l.Province)
            .IsRequired()
            .HasMaxLength(Location.MaxCityOrProvinceLength);

        builder.Property(l => l.CountryCode)
            .IsRequired()
            .HasMaxLength(Location.MaxCountryCodeLength)
            .HasDefaultValue("ES");

        builder.OwnsOne(l => l.Coordinate, coord =>
        {
            coord.Property(c => c.Latitude)
                .HasColumnName("Latitude")
                .HasPrecision(10, 7)
                .IsRequired();

            coord.Property(c => c.Longitude)
                .HasColumnName("Longitude")
                .HasPrecision(10, 7)
                .IsRequired();
        });

        builder.HasIndex(l => l.City);
        builder.HasIndex(l => l.Province);
        builder.HasIndex(l => l.CountryCode);
    }
}