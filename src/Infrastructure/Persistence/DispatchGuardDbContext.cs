using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Persistence;

public class DispatchGuardDbContext : DbContext
{
    private readonly IAuditOrigin? _origin;

    public DispatchGuardDbContext(DbContextOptions<DispatchGuardDbContext> options, IAuditOrigin? origin = null)
        : base(options)
    {
        _origin = origin;
    }

    #region DbSets
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<TripStop> TripStops => Set<TripStop>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<DriverUnavailability> DriverUnavailabilities => Set<DriverUnavailability>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleUnavailability> VehicleUnavailabilities => Set<VehicleUnavailability>();
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<CachedRoute> CachedRoutes => Set<CachedRoute>();
    public DbSet<OperatingBase> Bases => Set<OperatingBase>();
    public DbSet<AllocationAttempt> AllocationAttempts => Set<AllocationAttempt>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitEvent> VisitEvents => Set<VisitEvent>();
    public DbSet<RequestTrace> RequestTraces => Set<RequestTrace>();
    #endregion

    /// <summary>A change that cannot be audited is not saved (SPEC 0085 R2).</summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changes = _origin is { IsDemoLoad: true } ? null : AuditedChanges.Of(ChangeTracker);
        if (changes is not { Any: true })
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var saved = Database.CurrentTransaction is null
            ? await Database.CreateExecutionStrategy().ExecuteAsync(token => SaveInTransactionAsync(changes, token), cancellationToken)
            : await SaveAndAuditAsync(changes, cancellationToken);
        if (acceptAllChangesOnSuccess)
        {
            ChangeTracker.AcceptAllChanges();
        }

        return saved;
    }

    /// <summary>Only tests and tools save without waiting.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    /// <summary>In the open transaction, or on its own.</summary>
    internal Task AuditAsync(string action, string entity, CancellationToken cancellationToken) =>
        AuditAsync([(action, entity, null, "{}")], cancellationToken);

    private async Task<int> SaveInTransactionAsync(AuditedChanges changes, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var saved = await SaveAndAuditAsync(changes, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    /// <summary>The changes are not accepted until everything is written: whoever tries again needs them as they were.</summary>
    private async Task<int> SaveAndAuditAsync(AuditedChanges changes, CancellationToken cancellationToken)
    {
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
        await AuditAsync(changes.ToEvents(), cancellationToken);
        return saved;
    }

    private async Task AuditAsync(IReadOnlyList<(string Action, string Entity, int? Id, string Changes)> events, CancellationToken cancellationToken)
    {
        var name = _origin?.DeclaredName;
        await Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "AuditEvents" ("At", "SessionId", "TraceId", "DeclaredName", "Action", "Entity", "EntityId", "Changes")
            SELECT now(), {_origin?.SessionId}, {_origin?.TraceId}, {name}, e.action, e.entity, e.id, e.changes
            FROM unnest({events.Select(e => e.Action).ToArray()}, {events.Select(e => e.Entity).ToArray()},
                {events.Select(e => e.Id).ToArray()}, {events.Select(e => e.Changes).ToArray()}) AS e(action, entity, id, changes)
            """,
            cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcInstantConverter>();
        configurationBuilder.Properties<string>().UseCollation(SpanishCollation.Name);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        SpanishCollation.AddTo(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DispatchGuardDbContext).Assembly);
    }
}
