using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

/// <summary>The default base (Id 1) is created by the migration.</summary>
public class OperatingBaseConfiguration : EntityConfiguration<OperatingBase>
{
    public override void Configure(EntityTypeBuilder<OperatingBase> builder)
    {
        base.Configure(builder);
        builder.ToTable("Bases");
        builder.Property(b => b.Name).IsRequired().HasMaxLength(OperatingBase.NameMaxLength);
        builder.HasIndex(b => b.Name).IsUnique();
    }
}
