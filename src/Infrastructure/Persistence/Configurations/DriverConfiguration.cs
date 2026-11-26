using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class DriverConfiguration : EntityConfiguration<Driver>
{
    public override void Configure(EntityTypeBuilder<Driver> builder)
    {
        base.Configure(builder);

        // The rows that existed before bases went to the default one (SPEC 0050).
        builder.Property(x => x.BaseId).IsRequired().HasDefaultValue(OperatingBase.DefaultId);
        builder.HasOne<OperatingBase>().WithMany().HasForeignKey(x => x.BaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BaseId);

        builder.ToTable("Drivers");

        builder.Property(d => d.IdentificationNumber)
            .IsRequired()
            .HasMaxLength(Driver.MaxIdentificationNumberLength);

        builder.Property(d => d.FirstName)
            .IsRequired()
            .HasMaxLength(Driver.MaxNameLength);

        builder.Property(d => d.LastName)
            .IsRequired()
            .HasMaxLength(Driver.MaxNameLength);

        builder.Property(d => d.IsExternal)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(d => d.AllowsAutoAssignment)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(d => d.HireDate);
        builder.Property(d => d.TerminationDate);
        builder.Property(d => d.DeclaredLastWeeklyRestEnd);

        builder.HasOne(d => d.HomeLocation)
            .WithMany()
            .HasForeignKey(d => d.HomeLocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.OwnsOne(d => d.CurrentLimits, limits =>
        {
            limits.Property(l => l.DrivingToday)
                .HasColumnName("DrivingToday");

            limits.Property(l => l.DrivingThisWeek)
                .HasColumnName("DrivingThisWeek");

            limits.Property(l => l.DrivingLastTwoWeeks)
                .HasColumnName("DrivingLastTwoWeeks");

            limits.Property(l => l.ExtendedDaysUsedThisWeek)
                .HasColumnName("ExtendedDaysUsedThisWeek");

            limits.Property(l => l.ReducedDailyRestsUsed)
                .HasColumnName("ReducedDailyRestsUsed");

            limits.Property(l => l.LastDailyRestDate)
                .HasColumnName("LastDailyRestDate");

            limits.Property(l => l.LastDailyRestType)
                .HasColumnName("LastDailyRestType")
                .HasConversion<string>();

            limits.Property(l => l.LastWeeklyRestDate)
                .HasColumnName("LastWeeklyRestDate");

            limits.Property(l => l.LastWeeklyRestType)
                .HasColumnName("LastWeeklyRestType")
                .HasConversion<string>();

            limits.Property(l => l.CalculatedAt)
                .HasColumnName("LimitsCalculatedAt");
        });

        builder.HasIndex(d => d.IdentificationNumber).IsUnique();
        builder.HasIndex(d => d.IsExternal);
        builder.HasIndex(d => d.AllowsAutoAssignment);
    }
}