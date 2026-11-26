using System.Reflection;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

public class VehicleCandidateSearchTests
{
    private static readonly DateTime TripStart = new(2026, 12, 1, 8, 0, 0);

    private static readonly Location Origin = NewLocation(1, 40.4168m, -3.7038m);
    private static readonly Location Destination = NewLocation(2, 40.9000m, -3.7038m);

    private static Location NewLocation(int id, decimal lat, decimal lon)
    {
        var location = new Location($"L{id}", "Main St 1", new GeoCoordinate(lat, lon), "Madrid", "Madrid");
        SetId(location, id);
        return location;
    }

    private static Location KmNorth(int id, double km) => NewLocation(id, Origin.Coordinate.Latitude + (decimal)(km / 111.195), -3.7038m);

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);

    private static Vehicle NewVehicle(int id, Location? home, int seats = 50, bool allowsAuto = true, bool external = false)
    {
        var vehicle = new Vehicle($"PLATE{id}", "Brand", "Model", seats, external, allowsAuto);
        SetId(vehicle, id);
        if (home != null)
        {
            vehicle.SetHomeLocation(home);
        }

        return vehicle;
    }

    private static Trip NewTrip(int seats = 10, int id = 100, Location? origin = null, Location? destination = null, DateTime? start = null)
    {
        var tripStart = start ?? TripStart;
        var trip = new Trip(origin ?? Origin, destination ?? Destination, new TimeWindow(tripStart, tripStart.AddHours(3)), seats, ServiceType.Regular);
        SetId(trip, id);
        return trip;
    }

    private static VehicleCandidateSearch NewSearch(IEnumerable<Vehicle> vehicles, Dictionary<int, List<Trip>>? tripsByVehicle = null) =>
        new(new FakeVehicleRepository(vehicles), new FakeTripRepository(tripsByVehicle ?? []),
            new VehicleEligibilityService(), new FakeLocationRepository());

    [Fact]
    public async Task FindCandidates_InsufficientCapacity_IsExcluded()
    {
        var small = NewVehicle(1, Origin, seats: 8);
        var big = NewVehicle(2, KmNorth(10, 30), seats: 55);

        var result = await NewSearch([small, big]).FindCandidates(NewTrip(seats: 20));

        Assert.Equal([2], result.Select(c => c.Vehicle.Id));
    }

    [Fact]
    public async Task FindCandidates_DecommissionedOrUnavailable_IsExcluded()
    {
        var decommissioned = NewVehicle(1, Origin);
        decommissioned.Decommission(DateTime.UtcNow.AddDays(-1));
        var inWorkshop = NewVehicle(2, Origin);
        inWorkshop.AddUnavailability(new VehicleUnavailability(2, new TimeWindow(TripStart.AddHours(-1), TripStart.AddHours(1)), VehicleUnavailabilityType.Repair));
        var ok = NewVehicle(3, KmNorth(10, 15));

        var result = await NewSearch([decommissioned, inWorkshop, ok]).FindCandidates(NewTrip());

        Assert.Equal([3], result.Select(c => c.Vehicle.Id));
    }

    [Fact]
    public async Task FindCandidates_NotAutoAssignableExternalOrCurrent_IsExcluded()
    {
        var manual = NewVehicle(1, Origin, allowsAuto: false);
        var external = NewVehicle(2, Origin, external: true);
        var current = NewVehicle(3, Origin);
        var ok = NewVehicle(4, Origin);
        var trip = NewTrip();
        trip.AssignResources(new Driver("D1", "A", "B"), current);

        var result = await NewSearch([manual, external, current, ok]).FindCandidates(trip);

        Assert.Equal([4], result.Select(c => c.Vehicle.Id));
    }

    [Fact]
    public async Task FindCandidates_OverlappingTrip_IsExcluded_CancelledIsIgnored()
    {
        var busy = NewVehicle(1, Origin);
        var freedByCancel = NewVehicle(2, Origin);
        var overlapping = NewTrip(id: 200, start: TripStart.AddHours(1));
        var cancelled = NewTrip(id: 201, start: TripStart.AddHours(1));
        cancelled.Cancel();

        var result = await NewSearch([busy, freedByCancel], new() { [1] = [overlapping], [2] = [cancelled] }).FindCandidates(NewTrip());

        Assert.Equal([2], result.Select(c => c.Vehicle.Id));
    }

    [Fact]
    public async Task FindCandidates_LastTripEndsAtOrigin_RanksAtOrigin_AheadOfNearbyBase()
    {
        var farAway = KmNorth(20, 300);
        var comingBack = NewVehicle(2, farAway);         // base far away, but its previous trip ends at the origin (no empty run)
        var nearBase = NewVehicle(1, KmNorth(10, 3));
        var previous = NewTrip(id: 200, origin: farAway, destination: Origin, start: TripStart.AddHours(-5));

        var result = await NewSearch([nearBase, comingBack], new() { [2] = [previous] }).FindCandidates(NewTrip());

        Assert.Equal([2, 1], result.Select(c => c.Vehicle.Id));
        Assert.Equal(VehiclePositionSource.LastTripDestination, result[0].PositionSource);
        Assert.Equal(CandidateProximity.BaseAtOrigin, result[0].Proximity);
        Assert.StartsWith("Origen: destino de su último viaje (#200, ", result[0].Reason.Es());
        Assert.EndsWith(") = salida", result[0].Reason.Es());
        Assert.StartsWith("Origen: su base (", result[1].Reason.Es());
        Assert.Contains("; a ", result[1].Reason.Es());
        Assert.Equal(VehiclePositionSource.HomeBase, result[1].PositionSource);
    }

    [Fact]
    public async Task FindCandidates_PositionFromLatestPreviousTrip_NotFromBase()
    {
        // Base at the origin, but the vehicle was taken 300 km away and has not come back.
        var vehicle = NewVehicle(1, Origin);
        var far = KmNorth(20, 300);
        var outbound = NewTrip(id: 200, origin: Origin, destination: far, start: TripStart.AddDays(-1));
        var other = NewVehicle(2, KmNorth(10, 8));

        var result = await NewSearch([vehicle, other], new() { [1] = [outbound] }).FindCandidates(NewTrip());

        Assert.Equal([2, 1], result.Select(c => c.Vehicle.Id));
        Assert.Equal(CandidateProximity.Beyond40Km, result[1].Proximity);
    }

    [Fact]
    public async Task FindCandidates_NoTripsNoBase_RanksLast_WithReason()
    {
        var unknown = NewVehicle(1, null);
        var far = NewVehicle(2, KmNorth(10, 100));

        var result = await NewSearch([unknown, far]).FindCandidates(NewTrip());

        Assert.Equal([2, 1], result.Select(c => c.Vehicle.Id));
        Assert.Equal(VehiclePositionSource.Unknown, result[1].PositionSource);
        Assert.Equal(CandidateProximity.NoHomeLocation, result[1].Proximity);
        Assert.False(string.IsNullOrWhiteSpace(result[1].Reason.Es()));
    }

    [Fact]
    public async Task FindCandidates_RadiusBandsThenDistanceThenId()
    {
        var at12 = NewVehicle(1, KmNorth(11, 12));
        var at3 = NewVehicle(2, KmNorth(12, 3));
        var at3b = NewVehicle(3, KmNorth(13, 3));

        var result = await NewSearch([at12, at3b, at3]).FindCandidates(NewTrip());

        Assert.Equal([2, 3, 1], result.Select(c => c.Vehicle.Id));
        Assert.Equal(CandidateProximity.Within5Km, result[0].Proximity);
        Assert.Equal(CandidateProximity.Within20Km, result[2].Proximity);
    }

    [Fact]
    public async Task FindCandidates_ReturnsAtMostN_WithRanks()
    {
        var vehicles = Enumerable.Range(1, 5).Select(i => NewVehicle(i, KmNorth(10 + i, i))).ToList();

        var result = await NewSearch(vehicles).FindCandidates(NewTrip(), maxResults: 2);

        Assert.Equal([1, 2], result.Select(c => c.Rank));
        Assert.Equal([1, 2], result.Select(c => c.Vehicle.Id));
    }

    [Fact]
    public async Task FindCandidates_NobodyEligible_ReturnsEmpty()
    {
        var result = await NewSearch([NewVehicle(1, Origin, seats: 5)]).FindCandidates(NewTrip(seats: 30));

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindCandidatesForTrip_UnknownTrip_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() => NewSearch([]).FindCandidatesForTrip(999));
    }

    private sealed class FakeVehicleRepository(IEnumerable<Vehicle> vehicles) : IVehicleRepository
    {
        private readonly List<Vehicle> _vehicles = vehicles.ToList();
        public Task<IEnumerable<Vehicle>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Vehicle>>(_vehicles.Where(v => v.IsActive));
        public Task<Vehicle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(_vehicles.FirstOrDefault(v => v.Id == id));
        public Task<bool> ExistsByLicensePlateAsync(string licensePlate, int? exceptId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Vehicle> AddAsync(Vehicle entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Vehicle entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Vehicle entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeLocationRepository : ILocationRepository
    {
        public Task<Location?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Location>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Location> AddAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeTripRepository(Dictionary<int, List<Trip>> tripsByVehicle) : ITripRepository
    {
        public Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Trip>>([]);
        public Task<PagedResult<Trip>> GetByVehicleAsync(int vehicleId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default)
        {
            var items = tripsByVehicle.GetValueOrDefault(vehicleId, [])
                .Where(t => t.Id != excludeTripId && (to is null || t.ScheduledWindow.Start < to) && (from is null || t.ScheduledWindow.End > from))
                .ToList();
            return Task.FromResult(PagedResult<Trip>.Create(items, items.Count, page, pageSize));
        }

        public Task<Trip?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Trip?>(null);
        public Task<PagedResult<Trip>> GetByDriverAsync(int driverId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Trip>> GetByDateRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Trip>>([]);

        public Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PagedResult<Trip>> GetByStatusAsync(TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Trip>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Trip> AddAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
