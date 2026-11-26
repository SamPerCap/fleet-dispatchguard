using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

/// <summary>Ids are plain values, not foreign keys: the log outlives its trips.</summary>
public class AllocationAttemptConfiguration : EntityConfiguration<AllocationAttempt>
{
    public override void Configure(EntityTypeBuilder<AllocationAttempt> builder)
    {
        base.Configure(builder);
        builder.ToTable("AllocationAttempts");
        builder.Property(a => a.AttemptedBy).IsRequired().HasMaxLength(AllocationAttempt.AttemptedByMaxLength);
        builder.Property(a => a.Detail).HasMaxLength(AllocationAttempt.DetailMaxLength);
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(30);
        builder.Ignore(a => a.IsRejected);
        builder.Ignore(a => a.ShortSessionId);
        builder.Property(a => a.SessionId).HasMaxLength(AllocationAttempt.SessionIdLength).IsUnicode(false);
        builder.PrimitiveCollection(a => a.RuleIds).IsRequired();
        builder.Property(a => a.ForcedReason).HasMaxLength(AllocationAttempt.ForcedReasonMaxLength);
        builder.Property(a => a.Explanation);
        // Attempts older than the column were all chosen by a person (SPEC 0062).
        builder.Property(a => a.Origin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(AttemptOrigin.Manual).HasSentinel(AttemptOrigin.Manual);
        builder.HasIndex(a => new { a.BaseId, a.AttemptedAt });
    }
}
