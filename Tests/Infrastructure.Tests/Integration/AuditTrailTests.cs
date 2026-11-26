using System.Text.Json;
using DispatchGuard.Application.Services;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Demo;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0085 R2 and R4: what changes is audited in its own transaction, or does not change.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuditTrailTests(PostgreSqlFixture server) : IAsyncLifetime
{
    private static readonly Origin Visitor = new(new string('a', 32), new string('b', 32), "Lucía");

    private readonly ConcurrencyDatabase _database = new(server);

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Origin(string? SessionId, string? TraceId, string? DeclaredName, bool IsDemoLoad = false) : IAuditOrigin;

    /// <summary>As the API has it, with retries: they change how a transaction may be opened.</summary>
    private DispatchGuardDbContext Context(IAuditOrigin? origin = null) =>
        new(new DbContextOptionsBuilder<DispatchGuardDbContext>()
            .UseNpgsql(_database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(2, TimeSpan.FromMilliseconds(50), null)).Options, origin);

    private async Task<List<AuditEvent>> EventsAsync(string entity)
    {
        await using var context = Context();
        return await context.AuditEvents.AsNoTracking().Where(e => e.Entity == entity).OrderBy(e => e.Id).ToListAsync();
    }

    private async Task BreakTheAuditAsync() =>
        await ExecuteAsync("""ALTER TABLE "AuditEvents" ADD CONSTRAINT "Broken" CHECK (false) NOT VALID""");

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Vehicle NewVehicle()
    {
        var vehicle = TripRepositoryTests.NewVehicle();
        Assert.Equal(55, vehicle.SeatingCapacity);
        return vehicle;
    }

    private static JsonElement Field(AuditEvent audited, string name) =>
        JsonDocument.Parse(audited.Changes).RootElement.GetProperty(name);

    [Fact]
    public async Task Create_LeavesOneEventWithTheIdOfTheNewRow()
    {
        await using var context = Context(Visitor);
        var vehicle = NewVehicle();
        context.Vehicles.Add(vehicle);

        await context.SaveChangesAsync();

        var audited = Assert.Single(await EventsAsync(nameof(Vehicle)));
        Assert.True(vehicle.Id > 0);
        Assert.Equal(vehicle.Id, audited.EntityId);
        Assert.Equal(AuditActions.Created, audited.Action);
        Assert.Equal(Visitor.SessionId, audited.SessionId);
        Assert.Equal(Visitor.TraceId, audited.TraceId);
        Assert.Equal(Visitor.DeclaredName, audited.DeclaredName);
        Assert.Equal(55, Field(audited, "SeatingCapacity").GetProperty("New").GetInt32());
        Assert.InRange(DateTime.UtcNow - audited.At, TimeSpan.FromMinutes(-5), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Change_KeepsThePreviousAndTheNewValueOfWhatChanged()
    {
        await using var context = Context(Visitor);
        var vehicle = NewVehicle();
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        context.Entry(vehicle).Property(v => v.SeatingCapacity).CurrentValue = 38;

        await context.SaveChangesAsync();

        var audited = (await EventsAsync(nameof(Vehicle)))[1];
        Assert.Equal(AuditActions.Changed, audited.Action);
        Assert.Equal("""{"SeatingCapacity":{"Old":55,"New":38}}""", audited.Changes);
    }

    [Fact]
    public async Task Save_WithNothingChanged_LeavesNoEvent()
    {
        await using var context = Context(Visitor);
        context.Vehicles.Add(NewVehicle());
        await context.SaveChangesAsync();

        await context.SaveChangesAsync();

        Assert.Single(await EventsAsync(nameof(Vehicle)));
    }

    /// <summary>What is replaced whole, a schedule, is one field changed and not one deleted and another created.</summary>
    [Fact]
    public async Task Change_OfAValueObject_IsAuditedWithItsOwner()
    {
        var (_, _, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(
            _database.CreateContext, new DateTime(2035, 3, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: false);
        await using var context = Context(Visitor);
        var trip = await context.Trips.SingleAsync(t => t.Id == tripId);
        var window = trip.ScheduledWindow;
        context.Entry(trip).Reference(t => t.ScheduledWindow).CurrentValue = new TimeWindow(window.Start, window.End.AddHours(1));

        await context.SaveChangesAsync();

        var audited = Assert.Single(await EventsAsync(nameof(Trip)), e => e.SessionId is not null);
        Assert.Equal(tripId, audited.EntityId);
        Assert.Equal(AuditActions.Changed, audited.Action);
        Assert.Contains("ScheduledWindow.End", audited.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduledWindow.Start", audited.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersonalData_AppearsAsChangedWithoutItsValue()
    {
        await using var context = Context(Visitor);
        var driver = TripRepositoryTests.NewDriver();
        context.Drivers.Add(driver);

        await context.SaveChangesAsync();

        var audited = Assert.Single(await EventsAsync(nameof(Driver)));
        Assert.Contains("IdentificationNumber", audited.Changes, StringComparison.Ordinal);
        Assert.DoesNotContain(driver.IdentificationNumber, audited.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_ThatCannotBeWritten_LeavesTheChangeUnsaved()
    {
        await BreakTheAuditAsync();
        await using var context = Context(Visitor);
        context.Vehicles.Add(NewVehicle());

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());

        await using var read = Context();
        Assert.Empty(read.Vehicles);
    }

    [Fact]
    public async Task Change_RolledBack_TakesItsEventWithIt()
    {
        await using var context = _database.CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Vehicles.Add(NewVehicle());
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.Empty(await EventsAsync(nameof(Vehicle)));
    }

    [Fact]
    public async Task DemoLoad_IsNotAuditedRowByRow_AndTheResetLeavesOneEvent()
    {
        await using var context = Context(new Origin(null, null, null, IsDemoLoad: true));
        context.Vehicles.Add(NewVehicle());
        await context.SaveChangesAsync();

        await new DemoDataReset(context).ResetAsync();

        await using var read = Context();
        Assert.Equal(AuditActions.Reset, (await read.AuditEvents.AsNoTracking().SingleAsync()).Action);
    }

    [Fact]
    public async Task Reset_KeepsWhatIsRecordedOfTheVisits()
    {
        await using var context = Context(Visitor);
        context.Vehicles.Add(NewVehicle());
        await context.SaveChangesAsync();

        await new DemoDataReset(context).ResetAsync();

        Assert.Single(await EventsAsync(nameof(Vehicle)));
        Assert.Empty(context.Vehicles.AsNoTracking());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocation_ThatCannotBeRecorded_Fails(bool isRejected)
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(
            _database.CreateContext, new DateTime(2035, 5, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: isRejected);
        await using var context = Context(Visitor);

        await Assert.ThrowsAsync<AttemptNotRecordedException>(
            () => Allocation(context, new ThrowingLog()).AllocateDriver(tripId, driverId, vehicleId));

        await using var read = Context();
        Assert.Null((await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId)).AssignedDriverId);
        Assert.Empty(read.AllocationAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocation_WithoutAudit_Fails(bool isRejected)
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(
            _database.CreateContext, new DateTime(2035, 5, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: isRejected);
        await BreakTheAuditAsync();
        await using var context = Context(Visitor);

        await Assert.ThrowsAnyAsync<Exception>(
            () => Allocation(context, new AllocationAttemptLog(context)).AllocateDriver(tripId, driverId, vehicleId));

        await using var read = Context();
        Assert.Null((await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId)).AssignedDriverId);
        Assert.Empty(read.AllocationAttempts);
    }

    [Fact]
    public async Task Rejection_IsAuditedWithItsRules()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(
            _database.CreateContext, new DateTime(2035, 4, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: true);
        await using var context = Context(Visitor);

        await Assert.ThrowsAsync<VehicleIneligibleException>(
            () => Allocation(context, new AllocationAttemptLog(context)).AllocateDriver(tripId, driverId, vehicleId));

        var audited = Assert.Single(await EventsAsync(nameof(AllocationAttempt)));
        Assert.Contains("vehicle-unavailable", audited.Changes, StringComparison.Ordinal);
        Assert.Contains("RejectedVehicle", audited.Changes, StringComparison.Ordinal);
    }

    private static DriverAllocationService Allocation(DispatchGuardDbContext context, IAllocationAttemptLog log) => new(
        new LegalValidationService(new DrivingRecordRepository(context)),
        new VehicleEligibilityService(),
        new ScheduleConflictService(new TripRepository(context)),
        new UnitOfWork(context), log);

    private sealed class ThrowingLog : IAllocationAttemptLog
    {
        public Task RecordAsync(AllocationAttempt attempt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("log down");
    }
}
