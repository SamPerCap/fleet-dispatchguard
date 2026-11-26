using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// What InMemory and hand-written fakes cannot check: the EF model matches the migrations, aggregates round-trip and the driving
/// history query translates to SQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class PersistenceSmokeTests
{
    private readonly PostgreSqlFixture _db;

    public PersistenceSmokeTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task Migrations_ApplyCleanly_AndModelHasNoPendingChanges()
    {
        await using var context = _db.CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
        Assert.False(context.Database.HasPendingModelChanges(),
            "El modelo EF tiene cambios sin migración: ejecuta 'dotnet ef migrations add ...'.");
    }

    [Fact]
    public async Task Location_RoundTrips_WithCoordinate()
    {
        int id;
        await using (var write = _db.CreateContext())
        {
            var location = NewLocation("Atocha");
            write.Locations.Add(location);
            await write.SaveChangesAsync();
            id = location.Id;
        }

        await using var read = _db.CreateContext();
        var loaded = await read.Locations.SingleAsync(l => l.Id == id);

        Assert.Equal("Atocha", loaded.Name);
        Assert.Equal(40.4168m, loaded.Coordinate.Latitude);
        Assert.Equal(-3.7038m, loaded.Coordinate.Longitude);
    }

    [Fact]
    public async Task DrivingRecords_ReturnOnlyAssignedTripsOfDriverInsideRange()
    {
        var day = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        int driverId;

        await using (var write = _db.CreateContext())
        {
            var driver = new Driver($"ID-{Guid.NewGuid():N}"[..20], "Ana", "Pérez");
            var otherDriver = new Driver($"ID-{Guid.NewGuid():N}"[..20], "Luis", "Gómez");
            var vehicle = new Vehicle($"PL-{Guid.NewGuid():N}"[..12], "Irizar", "i6", 55);
            var origin = NewLocation("Origen");
            var destination = NewLocation("Destino");
            write.AddRange(driver, otherDriver, vehicle, origin, destination);
            await write.SaveChangesAsync();

            var inRange = NewTrip(origin, destination, day.AddHours(8), day.AddHours(12));
            inRange.AssignResources(driver, vehicle);
            var outOfRange = NewTrip(origin, destination, day.AddDays(3).AddHours(8), day.AddDays(3).AddHours(12));
            outOfRange.AssignResources(driver, vehicle);
            var otherDriversTrip = NewTrip(origin, destination, day.AddHours(14), day.AddHours(16));
            otherDriversTrip.AssignResources(otherDriver, vehicle);
            var unassigned = NewTrip(origin, destination, day.AddHours(18), day.AddHours(20));

            write.Trips.AddRange(inRange, outOfRange, otherDriversTrip, unassigned);
            await write.SaveChangesAsync();
            driverId = driver.Id;
        }

        await using var read = _db.CreateContext();
        var records = await new DrivingRecordRepository(read)
            .GetRecordsAsync(driverId, day, day.AddDays(1));

        var period = Assert.Single(records);
        Assert.Equal(day.AddHours(8), period.Start);
        Assert.Equal(day.AddHours(12), period.End);
    }

    private static Location NewLocation(string name) =>
        new(name, $"{name} 1", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");

    private static Trip NewTrip(Location origin, Location destination, DateTime start, DateTime end) =>
        new(origin, destination, new TimeWindow(start, end), requiredSeats: 10, ServiceType.Regular);
}
