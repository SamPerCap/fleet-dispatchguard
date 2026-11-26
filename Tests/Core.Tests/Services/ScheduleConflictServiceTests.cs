using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

public class ScheduleConflictServiceTests
{
    private static Location NewLocation(string name = "Origin") =>
        new(name: name, address: "Main St 1", coordinate: new GeoCoordinate(40.4168m, -3.7038m),
            city: "Madrid", province: "Madrid");

    private static Trip NewTrip(TimeWindow window, int requiredSeats = 10) =>
        new(origin: NewLocation("Origin"), destination: NewLocation("Destination"),
            scheduledWindow: window, requiredSeats: requiredSeats, serviceType: ServiceType.Regular, description: "Test trip");

    private static TimeWindow Window(DateTime start, DateTime end) => new(start, end);

    /// <summary>
    /// Excludes <c>excludeTripId</c> itself, as the EF Core implementation does, so that the service under test must not need to
    /// filter it again.
    /// </summary>
    private sealed class FakeTripRepository : ITripRepository
    {
        public Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Trip>>([]);
        private readonly List<Trip> _driverTrips;
        private readonly List<Trip> _vehicleTrips;

        public FakeTripRepository(IEnumerable<Trip>? driverTrips = null, IEnumerable<Trip>? vehicleTrips = null)
        {
            _driverTrips = driverTrips?.ToList() ?? [];
            _vehicleTrips = vehicleTrips?.ToList() ?? [];
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
            int? excludeTripId = null, CancellationToken cancellationToken = default)
        {
            var items = _vehicleTrips.Where(t => excludeTripId is null || t.Id != excludeTripId).ToList();
            return Task.FromResult(PagedResult<Trip>.Create(items, items.Count, page, pageSize));
        }

        public Task<PagedResult<Trip>> GetByDateRangeAsync(
            DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Trip>>([]);

        public Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PagedResult<Trip>> GetByStatusAsync(
            TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task<Trip?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task<IEnumerable<Trip>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task<Trip> AddAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task UpdateAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");

        public Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by ScheduleConflictService tests.");
    }

    private static void SetTripId(Trip trip, int id)
    {
        var idProperty = typeof(DispatchGuard.Core.Entities.Base.Entity).GetProperty(nameof(Trip.Id))!;
        idProperty.SetValue(trip, id);
    }

    [Fact]
    public async Task CheckDriverConflict_ExactSameWindowAsAnotherTrip_HasConflict()
    {
        var existingWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));
        var existingTrip = NewTrip(existingWindow);
        SetTripId(existingTrip, 1);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));

        var result = await sut.CheckDriverConflict(driverId: 1, window: existingWindow);

        Assert.True(result.HasConflict);
        Assert.Contains(1, result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_PartialOverlap_HasConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 2);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 11, 0, 0), new DateTime(2026, 7, 10, 13, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow);

        Assert.True(result.HasConflict);
        Assert.Contains(2, result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_NoOverlap_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 3);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 14, 0, 0), new DateTime(2026, 7, 10, 16, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_AdjacentWindows_NoConflict()
    {
        var existingEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), existingEnd));
        SetTripId(existingTrip, 4);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));
        var candidateWindow = Window(existingEnd, new DateTime(2026, 7, 10, 14, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_ConflictingTripIsCancelled_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 5);
        existingTrip.Cancel();
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 11, 0, 0), new DateTime(2026, 7, 10, 13, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_ExcludeTripIdMatchesOnlyCandidate_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 6);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow, excludeTripId: 6);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckDriverConflict_MultipleOverlappingTrips_AllIdsReturned()
    {
        var tripA = NewTrip(Window(new DateTime(2026, 7, 10, 9, 0, 0), new DateTime(2026, 7, 10, 11, 0, 0)));
        SetTripId(tripA, 7);
        var tripB = NewTrip(Window(new DateTime(2026, 7, 10, 10, 30, 0), new DateTime(2026, 7, 10, 13, 0, 0)));
        SetTripId(tripB, 8);
        var sut = new ScheduleConflictService(new FakeTripRepository(driverTrips: [tripA, tripB]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));

        var result = await sut.CheckDriverConflict(driverId: 1, window: candidateWindow);

        Assert.True(result.HasConflict);
        Assert.Equal(2, result.ConflictingTripIds.Count);
        Assert.Contains(7, result.ConflictingTripIds);
        Assert.Contains(8, result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_ExactSameWindowAsAnotherTrip_HasConflict()
    {
        var existingWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));
        var existingTrip = NewTrip(existingWindow);
        SetTripId(existingTrip, 10);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: existingWindow);

        Assert.True(result.HasConflict);
        Assert.Contains(10, result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_PartialOverlap_HasConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 11);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 11, 0, 0), new DateTime(2026, 7, 10, 13, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow);

        Assert.True(result.HasConflict);
        Assert.Contains(11, result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_NoOverlap_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 12);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 14, 0, 0), new DateTime(2026, 7, 10, 16, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_AdjacentWindows_NoConflict()
    {
        var existingEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), existingEnd));
        SetTripId(existingTrip, 13);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));
        var candidateWindow = Window(existingEnd, new DateTime(2026, 7, 10, 14, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_ConflictingTripIsCancelled_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 14);
        existingTrip.Cancel();
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 11, 0, 0), new DateTime(2026, 7, 10, 13, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_ExcludeTripIdMatchesOnlyCandidate_NoConflict()
    {
        var existingTrip = NewTrip(Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0)));
        SetTripId(existingTrip, 15);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [existingTrip]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow, excludeTripId: 15);

        Assert.False(result.HasConflict);
        Assert.Empty(result.ConflictingTripIds);
    }

    [Fact]
    public async Task CheckVehicleConflict_MultipleOverlappingTrips_AllIdsReturned()
    {
        var tripA = NewTrip(Window(new DateTime(2026, 7, 10, 9, 0, 0), new DateTime(2026, 7, 10, 11, 0, 0)));
        SetTripId(tripA, 16);
        var tripB = NewTrip(Window(new DateTime(2026, 7, 10, 10, 30, 0), new DateTime(2026, 7, 10, 13, 0, 0)));
        SetTripId(tripB, 17);
        var sut = new ScheduleConflictService(new FakeTripRepository(vehicleTrips: [tripA, tripB]));
        var candidateWindow = Window(new DateTime(2026, 7, 10, 10, 0, 0), new DateTime(2026, 7, 10, 12, 0, 0));

        var result = await sut.CheckVehicleConflict(vehicleId: 1, window: candidateWindow);

        Assert.True(result.HasConflict);
        Assert.Equal(2, result.ConflictingTripIds.Count);
        Assert.Contains(16, result.ConflictingTripIds);
        Assert.Contains(17, result.ConflictingTripIds);
    }
}
