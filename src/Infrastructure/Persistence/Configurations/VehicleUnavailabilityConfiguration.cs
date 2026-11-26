using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class VehicleUnavailabilityConfiguration : IEntityTypeConfiguration<VehicleUnavailability>
{
    public void Configure(EntityTypeBuilder<VehicleUnavailability> builder)
    {
        builder.ToTable("VehicleUnavailabilities");

        builder.HasKey(vu => vu.Id);

        builder.Property(vu => vu.Id)
            .ValueGeneratedOnAdd();

        builder.HasOne(vu => vu.Vehicle)
            .WithMany(v => v.Unavailabilities)
            .HasForeignKey(vu => vu.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(vu => vu.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(vu => vu.Notes)
            .HasMaxLength(AgendaEntryRules.MaxNotesLength);

        builder.Property(vu => vu.ServiceProvider)
            .HasMaxLength(AgendaEntryRules.MaxServiceProviderLength);

        builder.OwnsOne(vu => vu.Period, period =>
        {
            period.Property(p => p.Start)
                .HasColumnName("PeriodStart")
                .IsRequired();

            period.Property(p => p.End)
                .HasColumnName("PeriodEnd")
                .IsRequired();
        });

        builder.OwnsOne(vu => vu.Audit, audit =>
        {
            audit.Property(a => a.CreatedAt)
                .HasColumnName("CreatedAt")
                .IsRequired();

            audit.Property(a => a.UpdatedAt)
                .HasColumnName("UpdatedAt")
                .IsRequired(false);
        });

        builder.HasIndex(vu => vu.VehicleId);
        builder.HasIndex(vu => vu.Type);
    }
}