using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// The database is shared by the whole collection, so every test seeds its own driver and vehicle, uses its own dates and asserts
/// only on its own rows.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class TripRepositoryTests
{
    private readonly PostgreSqlFixture _db;

    public TripRepositoryTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task GetByDriverAsync_ReturnsOnlyTripsForThatDriver_PagedWithCorrectTotalCount()
    {
        var day = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var seed = await SeedAsync();
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            for (var i = 0; i < 15; i++)
            {
                write.Trips.Add(Assigned(seed, seed.Driver, seed.Vehicle, day.AddDays(i).AddHours(8), day.AddDays(i).AddHours(10)));
            }
            write.Trips.Add(Assigned(seed, seed.OtherDriver, seed.OtherVehicle, day.AddHours(8), day.AddHours(10)));
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByDriverAsync(seed.Driver.Id, from: null, to: null, page: 1, pageSize: 10);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(15, result.TotalCount);
        Assert.All(result.Items, trip => Assert.Equal(seed.Driver.Id, trip.AssignedDriverId));
        Assert.Equal(result.Items.OrderBy(t => t.ScheduledWindow.Start).Select(t => t.Id), result.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task GetByDriverAsync_WithDateRange_ExcludesTripsOutsideRange()
    {
        var seed = await SeedAsync();
        Trip inRange;
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            inRange = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2030, 7, 10, 8), Utc(2030, 7, 10, 10));
            var outOfRange = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2030, 8, 10, 8), Utc(2030, 8, 10, 10));
            write.Trips.AddRange(inRange, outOfRange);
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByDriverAsync(
            seed.Driver.Id, Utc(2030, 7, 1, 0), Utc(2030, 7, 31, 0), page: 1, pageSize: 10);

        var single = Assert.Single(result.Items);
        Assert.Equal(inRange.Id, single.Id);
    }

    [Theory]
    [InlineData(12, 0, false)] // [12:00, 14:00) touches the trip's end at 12:00: open limit, no overlap.
    [InlineData(11, 59, true)] // [11:59, 14:00) does overlap.
    public async Task GetByDriverAsync_RangeTouchingTripEnd_OverlapsOnlyWhenStrictlyInside(int fromHour, int fromMinute, bool expected)
    {
        var day = Utc(2030, 9, 2, 0);
        var seed = await SeedAsync();
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            write.Trips.Add(Assigned(seed, seed.Driver, seed.Vehicle, day.AddHours(8), day.AddHours(12)));
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByDriverAsync(
            seed.Driver.Id, day.AddHours(fromHour).AddMinutes(fromMinute), day.AddHours(14), page: 1, pageSize: 10);

        Assert.Equal(expected ? 1 : 0, result.TotalCount);
    }

    [Fact]
    public async Task GetByVehicleAsync_WithExcludeTripId_DoesNotIncludeExcludedTrip()
    {
        var seed = await SeedAsync();
        Trip kept, excluded;
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            kept = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2030, 10, 1, 8), Utc(2030, 10, 1, 10));
            excluded = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2030, 10, 2, 8), Utc(2030, 10, 2, 10));
            var otherVehicle = Assigned(seed, seed.OtherDriver, seed.OtherVehicle, Utc(2030, 10, 1, 8), Utc(2030, 10, 1, 10));
            write.Trips.AddRange(kept, excluded, otherVehicle);
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByVehicleAsync(
            seed.Vehicle.Id, from: null, to: null, page: 1, pageSize: 10, excludeTripId: excluded.Id);

        var single = Assert.Single(result.Items);
        Assert.Equal(kept.Id, single.Id);
        Assert.Equal(seed.Vehicle.Id, single.AssignedVehicleId);
    }

    [Fact]
    public async Task GetByDateRangeAsync_IncludesTripsThatPartiallyOverlapRequestedRange()
    {
        var seed = await SeedAsync();
        Trip straddling, outside;
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            // Starts before the range and ends inside it.
            straddling = Pending(seed, Utc(2031, 7, 4, 22), Utc(2031, 7, 5, 2));
            outside = Pending(seed, Utc(2031, 9, 1, 8), Utc(2031, 9, 1, 10));
            write.Trips.AddRange(straddling, outside);
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByDateRangeAsync(Utc(2031, 7, 5, 0), Utc(2031, 7, 6, 0), page: 1, pageSize: 10);

        Assert.Contains(result.Items, t => t.Id == straddling.Id);
        Assert.DoesNotContain(result.Items, t => t.Id == outside.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_ReturnsOnlyTripsWithMatchingStatus()
    {
        var seed = await SeedAsync();
        Trip pending, assigned;
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            pending = Pending(seed, Utc(2032, 1, 1, 8), Utc(2032, 1, 1, 10));
            assigned = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2032, 1, 2, 8), Utc(2032, 1, 2, 10));
            write.Trips.AddRange(pending, assigned);
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByStatusAsync(TripStatus.Pending, page: 1, pageSize: int.MaxValue);

        Assert.All(result.Items, trip => Assert.Equal(TripStatus.Pending, trip.Status));
        Assert.Contains(result.Items, trip => trip.Id == pending.Id);
        Assert.DoesNotContain(result.Items, trip => trip.Id == assigned.Id);
    }

    [Fact]
    public async Task GetByStatusAsync_PageBeyondAvailableResults_ReturnsEmptyItemsWithCorrectTotalCount()
    {
        var seed = await SeedAsync();
        int before;
        await using (var count = _db.CreateContext())
        {
            before = (await new TripRepository(count).GetByStatusAsync(TripStatus.Completed, page: 1, pageSize: 1)).TotalCount;
        }

        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            for (var i = 0; i < 5; i++)
            {
                var trip = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2033, 1, 1 + i, 8), Utc(2033, 1, 1 + i, 10));
                trip.Complete();
                write.Trips.Add(trip);
            }
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var result = await new TripRepository(read).GetByStatusAsync(TripStatus.Completed, page: 999, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(before + 5, result.TotalCount);
    }

    [Fact]
    public async Task DeleteAsync_AssignedTrip_IsCancelledAndInactive()
    {
        var seed = await SeedAsync();
        Trip trip;
        await using (var write = _db.CreateContext())
        {
            Attach(write, seed);
            trip = Assigned(seed, seed.Driver, seed.Vehicle, Utc(2033, 6, 1, 8), Utc(2033, 6, 1, 10));
            write.Trips.Add(trip);
            await write.SaveChangesAsync();
        }

        await using (var delete = _db.CreateContext())
        {
            var repository = new TripRepository(delete);
            var loaded = await repository.GetByIdAsync(trip.Id);
            await repository.DeleteAsync(loaded!);
            await delete.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var row = await new TripRepository(read).GetByIdAsync(trip.Id);
        Assert.NotNull(row);
        Assert.False(row.IsActive);
        Assert.Equal(TripStatus.Cancelled, row.Status);
    }

    [Fact]
    public async Task GetByDriverAsync_ToBeforeFrom_Throws()
    {
        await using var context = _db.CreateContext();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new TripRepository(context)
            .GetByDriverAsync(1, Utc(2030, 2, 2, 0), Utc(2030, 2, 1, 0), page: 1, pageSize: 10));
    }

    private sealed record Seed(Driver Driver, Driver OtherDriver, Vehicle Vehicle, Vehicle OtherVehicle, Location Origin, Location Destination);

    private async Task<Seed> SeedAsync()
    {
        await using var context = _db.CreateContext();
        var seed = new Seed(
            NewDriver(), NewDriver(), NewVehicle(), NewVehicle(),
            NewLocation("Origen"), NewLocation("Destino"));
        context.AddRange(seed.Driver, seed.OtherDriver, seed.Vehicle, seed.OtherVehicle, seed.Origin, seed.Destination);
        await context.SaveChangesAsync();
        return seed;
    }

    /// <summary>Seed entities come from another context: attach them as Unchanged.</summary>
    private static void Attach(DispatchGuardDbContext context, Seed seed) =>
        context.AttachRange(seed.Driver, seed.OtherDriver, seed.Vehicle, seed.OtherVehicle, seed.Origin, seed.Destination);

    private static Trip Pending(Seed seed, DateTime start, DateTime end) =>
        new(seed.Origin, seed.Destination, new TimeWindow(start, end), requiredSeats: 10, ServiceType.Regular);

    private static Trip Assigned(Seed seed, Driver driver, Vehicle vehicle, DateTime start, DateTime end)
    {
        var trip = Pending(seed, start, end);
        trip.AssignResources(driver, vehicle);
        return trip;
    }

    internal static Driver NewDriver() => new($"ID-{Guid.NewGuid():N}"[..20], "Ana", "Pérez");

    internal static Vehicle NewVehicle() => new($"PL-{Guid.NewGuid():N}"[..12], "Irizar", "i6", 55);

    internal static Location NewLocation(string name) =>
        new(name, $"{name} 1", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");

    private static DateTime Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
