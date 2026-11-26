using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

/// <summary>Return-to-base rule of Amendment 2020/1054 (SPEC 0016). Only completed trips count.</summary>
[Trait("Regulation", "Amend. 2020/1054")]
public class ReturnToBaseServiceTests
{
    private static Location NewLocation(string name = "Base") =>
        new(name: name, address: "Main St 1", coordinate: new GeoCoordinate(40.4168m, -3.7038m),
            city: "Madrid", province: "Madrid");

    private static Trip NewTrip(Location origin, Location destination, TimeWindow window, int requiredSeats = 10) =>
        new(origin: origin, destination: destination, scheduledWindow: window, requiredSeats: requiredSeats, serviceType: ServiceType.Regular, description: "Test trip");

    private static TimeWindow Window(DateTime start, DateTime end) => new(start, end);

    private static void SetEntityId(Entity entity, int id)
    {
        var idProperty = typeof(Entity).GetProperty(nameof(Entity.Id))!;
        idProperty.SetValue(entity, id);
    }

    private static Driver NewDriver() => new(
        identificationNumber: "12345678A",
        firstName: "John",
        lastName: "Doe");

    private sealed class FakeDriverRepository : IDriverRepository
    {
        public Task<bool> ExistsByIdentificationNumberAsync(string identificationNumber, int? exceptId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_drivers.Values.Any(d => d.IdentificationNumber == identificationNumber && d.Id != exceptId));

        private readonly Dictionary<int, Driver> _drivers;

        public FakeDriverRepository(Driver? driver = null)
        {
            _drivers = new Dictionary<int, Driver>();
            if (driver is not null)
            {
                _drivers[driver.Id] = driver;
            }
        }

        public Task<Driver?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_drivers.TryGetValue(id, out var driver) ? driver : null);

        public Task<IEnumerable<Driver>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<Driver> AddAsync(Driver entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task UpdateAsync(Driver entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task DeleteAsync(Driver entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");
    }

    /// <summary>
    /// Ignores the range on purpose: the 28-day boundary must be enforced by <see cref="ReturnToBaseService"/> itself, not by the
    /// repository.
    /// </summary>
    private sealed class FakeTripRepository : ITripRepository
    {
        public Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Trip>>([]);
        private readonly List<Trip> _driverTrips;

        public FakeTripRepository(IEnumerable<Trip>? driverTrips = null)
        {
            _driverTrips = driverTrips?.ToList() ?? [];
        }

        public Task<PagedResult<Trip>> GetByDriverAsync(
            int driverId, DateTime? from, DateTime? to, int page, int pageSize,
            int? excludeTripId = null, CancellationToken cancellationToken = default)
        {
            var items = _driverTrips.Where(t => excludeTripId is null || t.Id != excludeTripId).ToList();
            return Task.FromResult(PagedResult<Trip>.Create(items, items.Count, page, pageSize));
        }

        public Task<PagedResult<Trip>> GetByVehicleAsync(
            int vehicleId, DateTime? from, DateTime? to, int page, int pageSize,
            int? excludeTripId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<PagedResult<Trip>> GetByDateRangeAsync(
            DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Trip>>([]);

        public Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PagedResult<Trip>> GetByStatusAsync(
            TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<Trip?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<IEnumerable<Trip>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task<Trip> AddAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task UpdateAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");

        public Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ReturnToBaseService tests.");
    }

    [Fact]
    public async Task CheckCompliance_TripReturnsToBaseAsDestinationWithinWindow_ReturnsCompliant()
    {
        // A completed trip 10 days ago ends at the driver's home base.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var returnDate = asOf.AddDays(-10);
        var trip = NewTrip(elsewhere, baseLocation, Window(returnDate, returnDate.AddHours(2)));
        trip.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([trip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.Compliant, result.Status);
        Assert.Equal(returnDate, result.LastReturnDate);
        Assert.Equal(10, result.DaysSinceLastReturn);
        Assert.True(result.IsCompliant);
    }

    [Fact]
    public async Task CheckCompliance_TripReturnsToBaseAsOriginWithinWindow_ReturnsCompliant()
    {
        // As above, but the trip departs from the home base: the origin counts the same as the destination.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var returnDate = asOf.AddDays(-5);
        var trip = NewTrip(baseLocation, elsewhere, Window(returnDate, returnDate.AddHours(2)));
        trip.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([trip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.Compliant, result.Status);
        Assert.Equal(returnDate, result.LastReturnDate);
        Assert.Equal(5, result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_NoTripTouchesBaseWithinWindow_ReturnsNonCompliantWithNullValues()
    {
        // No trip in the last 28 days touches the home base.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhereA = NewLocation("ElsewhereA");
        SetEntityId(elsewhereA, 200);
        var elsewhereB = NewLocation("ElsewhereB");
        SetEntityId(elsewhereB, 300);

        var trip = NewTrip(elsewhereA, elsewhereB, Window(asOf.AddDays(-5), asOf.AddDays(-5).AddHours(2)));
        trip.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([trip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.NonCompliant, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public async Task CheckCompliance_DriverHasNoHomeLocation_ReturnsNotVerifiable()
    {
        var driver = NewDriver();
        SetEntityId(driver, 1);

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository());

        var result = await sut.CheckCompliance(driverId: 1, asOf: new DateTime(2026, 7, 3));

        Assert.Equal(ReturnToBaseStatus.NotVerifiable, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_DriverIdNotFound_ReturnsNotVerifiableWithoutThrowing()
    {
        var sut = new ReturnToBaseService(
            new FakeDriverRepository(),
            new FakeTripRepository());

        var result = await sut.CheckCompliance(driverId: 999, asOf: new DateTime(2026, 7, 3));

        Assert.Equal(ReturnToBaseStatus.NotVerifiable, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_TripExactlyOn28thDay_CountsAsCompliant()
    {
        // A trip that starts exactly at asOf - 28 days still counts.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var returnDate = asOf.AddDays(-28);
        var trip = NewTrip(elsewhere, baseLocation, Window(returnDate, returnDate.AddHours(2)));
        trip.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([trip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.Compliant, result.Status);
        Assert.Equal(returnDate, result.LastReturnDate);
        Assert.Equal(28, result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_TripOn29thDay_DoesNotCount_ReturnsNonCompliant()
    {
        // A trip that starts at asOf - 29 days falls outside the window.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var returnDate = asOf.AddDays(-29);
        var trip = NewTrip(elsewhere, baseLocation, Window(returnDate, returnDate.AddHours(2)));
        trip.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([trip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.NonCompliant, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_MultipleTripsTouchBase_LastReturnDateIsMostRecent()
    {
        // Three completed trips touch the base; only the most recent one determines the last return.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var oldestReturn = asOf.AddDays(-27);
        var middleReturn = asOf.AddDays(-15);
        var mostRecentReturn = asOf.AddDays(-3);

        var tripOldest = NewTrip(elsewhere, baseLocation, Window(oldestReturn, oldestReturn.AddHours(2)));
        tripOldest.Complete();
        var tripMiddle = NewTrip(baseLocation, elsewhere, Window(middleReturn, middleReturn.AddHours(2)));
        tripMiddle.Complete();
        var tripMostRecent = NewTrip(elsewhere, baseLocation, Window(mostRecentReturn, mostRecentReturn.AddHours(2)));
        tripMostRecent.Complete();

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([tripOldest, tripMiddle, tripMostRecent]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.Compliant, result.Status);
        Assert.Equal(mostRecentReturn, result.LastReturnDate);
        Assert.Equal(3, result.DaysSinceLastReturn);
    }

    [Fact]
    public async Task CheckCompliance_TripTouchesBaseButNotCompleted_DoesNotCount()
    {
        // A trip touching the base exists within the window but is still pending: it is not a valid return.
        var asOf = new DateTime(2026, 7, 3);
        var baseLocation = NewLocation("Base");
        SetEntityId(baseLocation, 100);

        var driver = NewDriver();
        SetEntityId(driver, 1);
        driver.SetHomeLocation(baseLocation);

        var elsewhere = NewLocation("Elsewhere");
        SetEntityId(elsewhere, 200);

        var returnDate = asOf.AddDays(-5);
        var pendingTrip = NewTrip(elsewhere, baseLocation, Window(returnDate, returnDate.AddHours(2)));
        // Left as TripStatus.Pending, never completed.

        var sut = new ReturnToBaseService(
            new FakeDriverRepository(driver),
            new FakeTripRepository([pendingTrip]));

        var result = await sut.CheckCompliance(driverId: 1, asOf: asOf);

        Assert.Equal(ReturnToBaseStatus.NonCompliant, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
    }
}
