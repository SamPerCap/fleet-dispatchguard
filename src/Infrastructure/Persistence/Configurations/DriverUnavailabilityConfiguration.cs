using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class DriverUnavailabilityConfiguration : IEntityTypeConfiguration<DriverUnavailability>
{
    public void Configure(EntityTypeBuilder<DriverUnavailability> builder)
    {
        builder.ToTable("DriverUnavailabilities");

        builder.HasKey(du => du.Id);

        builder.Property(du => du.Id)
            .ValueGeneratedOnAdd();

        builder.HasOne(du => du.Driver)
            .WithMany(d => d.Unavailabilities)
            .HasForeignKey(du => du.DriverId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(du => du.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(du => du.Notes)
            .HasMaxLength(AgendaEntryRules.MaxNotesLength);

        builder.OwnsOne(du => du.Period, period =>
        {
            period.Property(p => p.Start)
                .HasColumnName("PeriodStart")
                .IsRequired();

            period.Property(p => p.End)
                .HasColumnName("PeriodEnd")
                .IsRequired();
        });

        builder.OwnsOne(du => du.Audit, audit =>
        {
            audit.Property(a => a.CreatedAt)
                .HasColumnName("CreatedAt")
                .IsRequired();

            audit.Property(a => a.UpdatedAt)
                .HasColumnName("UpdatedAt")
                .IsRequired(false);
        });

        builder.HasIndex(du => du.DriverId);
        builder.HasIndex(du => du.Type);
    }
}