using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// No fakes: real services and repositories over PostgreSQL. Mirrors what the allocate endpoint does: load the three aggregates
/// through the repositories of one context, allocate, and check the database from a fresh context (SPEC 0034).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class DriverAllocationIntegrationTests
{
    private readonly PostgreSqlFixture _db;

    public DriverAllocationIntegrationTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task AllocateDriver_ValidDriverAndVehicle_AssignsAndPersists()
    {
        var start = new DateTime(2034, 3, 6, 8, 0, 0, DateTimeKind.Utc);
        var (driverId, vehicleId, tripId) = await SeedAsync(start, unavailableVehicle: false);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = CreateService(context);
            var trip = await uow.Trips.GetByIdAsync(tripId);
            var driver = await uow.Drivers.GetByIdAsync(driverId);
            var vehicle = await uow.Vehicles.GetByIdAsync(vehicleId);

            var result = await service.AllocateDriver(trip!.Id, driver!.Id, vehicle!.Id);

            Assert.True(result.Validation.IsCompliant);
        }

        await using var read = _db.CreateContext();
        var persisted = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.Equal(driverId, persisted.AssignedDriverId);
        Assert.Equal(vehicleId, persisted.AssignedVehicleId);
        Assert.Equal(TripStatus.Assigned, persisted.Status);
        Assert.NotNull(persisted.LegalValidation);
        Assert.True(persisted.LegalValidation.IsCompliant);
    }

    [Fact]
    public async Task AllocateDriver_NewDriverWithoutHistory_AssignsAndPersistsUnverifiableWarning()
    {
        // A brand-new driver is assignable; the saved validation carries the warning (SPEC 0040).
        var start = new DateTime(2034, 4, 6, 8, 0, 0, DateTimeKind.Utc);
        var (driverId, vehicleId, tripId) = await SeedAsync(start, unavailableVehicle: false, withHistory: false);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = CreateService(context);
            var trip = await uow.Trips.GetByIdAsync(tripId);
            var driver = await uow.Drivers.GetByIdAsync(driverId);
            var vehicle = await uow.Vehicles.GetByIdAsync(vehicleId);

            await service.AllocateDriver(trip!.Id, driver!.Id, vehicle!.Id);
        }

        await using var read = _db.CreateContext();
        var persisted = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.Equal(driverId, persisted.AssignedDriverId);
        Assert.True(persisted.LegalValidation!.IsCompliant);
        Assert.Contains(persisted.LegalValidation.Warnings,
            w => w.Es().StartsWith("Historial insuficiente para verificar el descanso semanal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllocateDriver_VehicleWithOverlappingUnavailability_ThrowsAndPersistsNothing()
    {
        var start = new DateTime(2034, 4, 6, 8, 0, 0, DateTimeKind.Utc);
        var (driverId, vehicleId, tripId) = await SeedAsync(start, unavailableVehicle: true);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = CreateService(context);
            var trip = await uow.Trips.GetByIdAsync(tripId);
            var driver = await uow.Drivers.GetByIdAsync(driverId);
            var vehicle = await uow.Vehicles.GetByIdAsync(vehicleId);

            // The unavailability only reaches the eligibility check if the repository loads it.
            Assert.Single(vehicle!.Unavailabilities);
            await Assert.ThrowsAsync<VehicleIneligibleException>(
                () => service.AllocateDriver(trip!.Id, driver!.Id, vehicle!.Id));
        }

        await using var read = _db.CreateContext();
        var persisted = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.Null(persisted.AssignedDriverId);
        Assert.Null(persisted.AssignedVehicleId);
        Assert.Equal(TripStatus.Pending, persisted.Status);
    }

    [Fact]
    public async Task AllocateDriver_DriverAlreadyOnOverlappingTrip_ThrowsScheduleConflict()
    {
        var start = new DateTime(2034, 5, 6, 8, 0, 0, DateTimeKind.Utc);
        var (driverId, vehicleId, tripId) = await SeedAsync(start, unavailableVehicle: false);

        // Another trip for the same driver overlapping the window, on a different vehicle.
        int busyTripId;
        await using (var write = _db.CreateContext())
        {
            var driver = await write.Drivers.SingleAsync(d => d.Id == driverId);
            var otherVehicle = TripRepositoryTests.NewVehicle();
            var origin = TripRepositoryTests.NewLocation("O");
            var destination = TripRepositoryTests.NewLocation("D");
            var busy = new Trip(origin, destination, new TimeWindow(start.AddHours(1), start.AddHours(3)), 10, ServiceType.Regular);
            busy.AssignResources(driver, otherVehicle);
            write.AddRange(otherVehicle, origin, destination, busy);
            await write.SaveChangesAsync();
            busyTripId = busy.Id;
        }

        await using var context = _db.CreateContext();
        var (service, uow) = CreateService(context);
        var trip = await uow.Trips.GetByIdAsync(tripId);
        var loadedDriver = await uow.Drivers.GetByIdAsync(driverId);
        var vehicle = await uow.Vehicles.GetByIdAsync(vehicleId);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(
            () => service.AllocateDriver(trip!.Id, loadedDriver!.Id, vehicle!.Id));
        Assert.Contains(busyTripId, ex.ConflictingTripIds);
    }

    private static (DriverAllocationService Service, UnitOfWork Uow) CreateService(DispatchGuardDbContext context)
    {
        var uow = new UnitOfWork(context);
        var service = new DriverAllocationService(
            new LegalValidationService(new DrivingRecordRepository(context)),
            new VehicleEligibilityService(),
            new ScheduleConflictService(new TripRepository(context)),
            uow);
        return (service, uow);
    }

    private Task<(int DriverId, int VehicleId, int TripId)> SeedAsync(DateTime start, bool unavailableVehicle, bool withHistory = true) =>
        SeedForLogAsync(_db, start, unavailableVehicle, withHistory);

    /// <summary>Also used by the attempt-log tests.</summary>
    internal static async Task<(int DriverId, int VehicleId, int TripId)> SeedForLogAsync(PostgreSqlFixture db, DateTime start, bool unavailableVehicle, bool withHistory = true) =>
        await SeedForLogAsync(db.CreateContext, start, unavailableVehicle, withHistory);

    internal static async Task<(int DriverId, int VehicleId, int TripId)> SeedForLogAsync(
        Func<DispatchGuardDbContext> createContext, DateTime start, bool unavailableVehicle, bool withHistory = true)
    {
        await using var write = createContext();
        var driver = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        write.AddRange(driver, vehicle, origin, destination);
        await write.SaveChangesAsync();

        // Without driving history the weekly rest is only "unverifiable" (SPEC 0040, warning). Two past trips with a gap of ≥ 45h between
        // them make the last weekly rest 2 days old, and nothing is driven in the last 24h.
        foreach (var daysBefore in withHistory ? new[] { 5, 2 } : Array.Empty<int>())
        {
            var past = new Trip(origin, destination,
                new TimeWindow(start.AddDays(-daysBefore), start.AddDays(-daysBefore).AddHours(2)), requiredSeats: 10, ServiceType.Regular);
            past.AssignResources(driver, vehicle);
            past.Complete();
            write.Trips.Add(past);
        }

        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(4)), requiredSeats: 30, ServiceType.Regular);
        write.Trips.Add(trip);
        if (unavailableVehicle)
        {
            vehicle.AddUnavailability(new VehicleUnavailability(
                vehicle.Id, new TimeWindow(start.AddDays(-1), start.AddDays(1)), VehicleUnavailabilityType.Repair));
        }
        await write.SaveChangesAsync();

        return (driver.Id, vehicle.Id, trip.Id);
    }
}
