using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

public class ClientConfiguration : EntityConfiguration<Client>
{
    public override void Configure(EntityTypeBuilder<Client> builder)
    {
        base.Configure(builder);
        builder.ToTable("Clients");

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(Client.MaxNameLength);

        builder.Property(c => c.TaxId)
            .HasMaxLength(50);

        builder.Property(c => c.ContactEmail)
            .HasMaxLength(255);

        builder.Property(c => c.ContactPhone)
            .HasMaxLength(20);

        builder.Property(c => c.IsExternal)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasMany(c => c.Trips)
            .WithOne()
            .HasForeignKey("ClientId")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.TaxId).IsUnique();
        builder.HasIndex(c => c.IsExternal);
    }
}