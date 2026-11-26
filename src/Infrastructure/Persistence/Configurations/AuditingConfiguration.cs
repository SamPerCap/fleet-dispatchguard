using DispatchGuard.Core.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DispatchGuard.Infrastructure.Persistence.Configurations;

/// <summary>Four tables where the application only adds (SPEC 0085 R3): without keys among them, so that each expires by itself.</summary>
public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.Property(e => e.SessionId).HasMaxLength(Visit.SessionIdLength);
        builder.Property(e => e.TraceId).HasMaxLength(RequestTrace.TraceIdLength);
        builder.Property(e => e.DeclaredName).HasMaxLength(AuditEvent.DeclaredNameMaxLength);
        builder.Property(e => e.Action).HasMaxLength(AuditEvent.ActionMaxLength);
        builder.Property(e => e.Entity).HasMaxLength(AuditEvent.EntityMaxLength);
        builder.HasIndex(e => e.SessionId);
    }
}

public sealed class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("Visits");
        builder.HasKey(v => v.SessionId);
        builder.Property(v => v.SessionId).HasMaxLength(Visit.SessionIdLength);
        builder.Property(v => v.Reference).HasMaxLength(Visit.ReferenceMaxLength);
    }
}

public sealed class VisitEventConfiguration : IEntityTypeConfiguration<VisitEvent>
{
    public void Configure(EntityTypeBuilder<VisitEvent> builder)
    {
        builder.ToTable("VisitEvents");
        builder.Property(e => e.SessionId).HasMaxLength(Visit.SessionIdLength);
        builder.Property(e => e.TraceId).HasMaxLength(RequestTrace.TraceIdLength);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(VisitEvent.KindMaxLength);
        builder.Property(e => e.Name).HasMaxLength(VisitEvent.NameMaxLength);
        builder.HasIndex(e => e.SessionId);
    }
}

public sealed class RequestTraceConfiguration : IEntityTypeConfiguration<RequestTrace>
{
    public void Configure(EntityTypeBuilder<RequestTrace> builder)
    {
        builder.ToTable("RequestTraces");
        builder.Property(t => t.SessionId).HasMaxLength(Visit.SessionIdLength);
        builder.Property(t => t.TraceId).HasMaxLength(RequestTrace.TraceIdLength);
        builder.Property(t => t.Method).HasMaxLength(RequestTrace.MethodMaxLength);
        builder.Property(t => t.Path).HasMaxLength(RequestTrace.PathMaxLength);
        builder.Property(t => t.Detail).HasMaxLength(RequestTrace.DetailMaxLength);
        builder.HasIndex(t => t.SessionId);
    }
}
