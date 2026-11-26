using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class AllocationAttemptLogIntegrationTests
{
    private readonly PostgreSqlFixture _db;

    public AllocationAttemptLogIntegrationTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task Migration_CreatesDefaultBase_AndNewEntitiesBelongToIt()
    {
        await using var context = _db.CreateContext();
        var defaultBase = await context.Bases.AsNoTracking().SingleAsync(b => b.Id == OperatingBase.DefaultId);
        Assert.Equal("Base principal", defaultBase.Name);

        var driver = TripRepositoryTests.NewDriver();
        context.Drivers.Add(driver);
        await context.SaveChangesAsync();
        Assert.Equal(OperatingBase.DefaultId, (await context.Drivers.AsNoTracking().SingleAsync(d => d.Id == driver.Id)).BaseId);
    }

    [Fact]
    public async Task AllocateDriver_Accepted_RecordsAssignedAttemptWithWho()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2035, 3, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: false);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, new AllocationAttemptLog(context));
            await service.AllocateDriver(tripId, driverId, vehicleId, default, "Lucía");
        }

        await using var read = _db.CreateContext();
        var attempt = await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId);
        Assert.Equal(AllocationOutcome.Assigned, attempt.Outcome);
        Assert.Equal("Lucía", attempt.AttemptedBy);
        Assert.Equal(driverId, attempt.DriverId);
        Assert.Equal(OperatingBase.DefaultId, attempt.BaseId);
        Assert.Null(attempt.Detail);
    }

    [Fact]
    public async Task AllocateDriver_Rejected_RecordsReasonAndInfringement()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2035, 4, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: true);
        VehicleIneligibleException rejection;

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, new AllocationAttemptLog(context));
            rejection = await Assert.ThrowsAsync<VehicleIneligibleException>(async () => await service.AllocateDriver(tripId, driverId, vehicleId));
        }

        await using var read = _db.CreateContext();
        var attempt = await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId);
        Assert.Equal(AllocationOutcome.RejectedVehicle, attempt.Outcome);
        Assert.Equal(AllocationAttempt.Anonymous, attempt.AttemptedBy);
        Assert.False(string.IsNullOrWhiteSpace(attempt.Detail));
        // The dispatcher's explanation (the failed check, in Spanish), not the exception's technical message.
        var failed = rejection.Explanation!.Business.First(i => i.Outcome == ExplanationOutcome.Failed);
        Assert.Equal($"{failed.Title.Es()}: {failed.Detail.Es()}", attempt.Detail);
        Assert.NotEqual(rejection.Message, attempt.Detail);
        Assert.Null((await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId)).AssignedDriverId);
    }

    private static (DriverAllocationService, UnitOfWork) Create(DispatchGuardDbContext context, IAllocationAttemptLog log)
    {
        var uow = new UnitOfWork(context);
        return (new DriverAllocationService(
            new LegalValidationService(new DrivingRecordRepository(context)),
            new VehicleEligibilityService(),
            new ScheduleConflictService(new TripRepository(context)),
            uow, log), uow);
    }
}
