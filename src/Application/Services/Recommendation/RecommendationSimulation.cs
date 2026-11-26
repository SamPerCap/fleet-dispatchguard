using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Recommendation;

/// <summary>In the order of the searches, with the reach of each one.</summary>
public sealed record SimulatedCandidates(
    IReadOnlyList<(DriverCandidate Candidate, ReachCandidate Reach)> Drivers,
    IReadOnlyList<(Vehicle Vehicle, ReachCandidate Reach)> Vehicles)
{
    public RecommendationPick? Pick() => RecommendationPick.Choose(Drivers.Select(d => d.Reach).ToList(), Vehicles.Select(v => v.Reach).ToList());
}

/// <summary>
/// The engine's own classes over a copy in memory (SPEC 0062 R8, ADR 0020): searches, CE 561/2006 with the later services,
/// availability, vehicle and conflicts. Each accepted pair is assigned on the copy and counts for the next ones.
/// </summary>
public sealed class RecommendationSimulation
{
    private readonly SimulatedTrips _trips;
    private readonly SimulatedDrivers _drivers;
    private readonly SimulatedVehicles _vehicles;
    private readonly SimulatedLocations _locations;
    private readonly AllocationEvaluator _evaluator;
    private readonly DriverCandidateSearch _driverSearch;
    private readonly VehicleCandidateSearch _vehicleSearch;

    public RecommendationSimulation(PlanningSnapshot snapshot, IVehicleEligibilityService eligibility, IDriverRankingPolicy? ranking = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _trips = new SimulatedTrips(snapshot.Trips);
        _drivers = new SimulatedDrivers(snapshot.Drivers);
        _vehicles = new SimulatedVehicles(snapshot.Vehicles);
        _locations = new SimulatedLocations(snapshot.Locations);
        var legal = new LegalValidationService(_trips);
        _evaluator = new AllocationEvaluator(legal, eligibility, new ScheduleConflictService(_trips));
        _driverSearch = new DriverCandidateSearch(_drivers, _trips, legal, _locations, ranking, new DriverHoursService(_trips));
        _vehicleSearch = new VehicleCandidateSearch(_vehicles, _trips, eligibility, _locations);
    }

    public Task<Trip?> TripAsync(int id) => _trips.GetByIdAsync(id);

    public Driver? Driver(int id) => _drivers.Find(id);

    public Vehicle? Vehicle(int id) => _vehicles.Find(id);

    public string LocationName(int id) => _locations.Find(id)?.Name ?? $"#{id}";

    /// <summary>The verdict assigning would give; when it accepts, the pair counts from now on.</summary>
    public async Task<AllocationVerdict> TryAssignAsync(Trip trip, Driver driver, Vehicle vehicle, CancellationToken ct = default)
    {
        var verdict = await _evaluator.EvaluateAsync(driver, vehicle, trip, cancellationToken: ct);
        if (verdict.IsAllowed) _trips.Assign(trip, driver, vehicle);
        return verdict;
    }

    public async Task<SimulatedCandidates> CandidatesAsync(Trip trip, CancellationToken ct = default)
    {
        var drivers = await _driverSearch.FindOptions(trip, ct);
        var vehicles = await _vehicleSearch.FindCandidates(trip, int.MaxValue, ct);
        var bookingVehicles = (await SiblingsAsync(trip, ct)).Select(t => t.AssignedVehicleId).ToHashSet();
        return new SimulatedCandidates(
            drivers.Candidates.Select(c => (c, ReachOf(trip, c.Driver, c.CoversSameBooking))).ToList(),
            vehicles.Select(v => (v.Vehicle, ReachOf(trip, v.Vehicle, bookingVehicles.Contains(v.Vehicle.Id)))).ToList());
    }

    /// <summary>The other live trips of the booking, with what the simulation has assigned them.</summary>
    public async Task<IReadOnlyList<Trip>> SiblingsAsync(Trip trip, CancellationToken ct = default) =>
        (await _trips.GetByBookingAsync(trip.BookingId, ct)).Where(t => t.Id != trip.Id && t.Status != TripStatus.Cancelled).ToList();

    public ReachCandidate ReachOf(Trip trip, Driver driver, bool sameBooking = false) =>
        new(driver.Id, Reach(trip, driver.HomeLocationId, _trips.OfDriver(driver.Id)), sameBooking);

    public ReachCandidate ReachOf(Trip trip, Vehicle vehicle, bool sameBooking = false) =>
        new(vehicle.Id, Reach(trip, vehicle.HomeLocationId, _trips.OfVehicle(vehicle.Id)), sameBooking);

    private ServiceReach Reach(Trip trip, int? homeLocationId, IEnumerable<Trip> services)
    {
        var origin = At(trip.OriginLocationId) ?? throw new InvalidOperationException($"Trip {trip.Id} has no origin location");
        var destination = At(trip.DestinationLocationId) ?? throw new InvalidOperationException($"Trip {trip.Id} has no destination location");
        var held = services
            .Where(t => t.Id != trip.Id && t.Status != TripStatus.Cancelled)
            .Select(t => (t, From: At(t.OriginLocationId), To: At(t.DestinationLocationId)))
            .Where(x => x.From is not null && x.To is not null)
            .Select(x => new HeldService(x.t.Id, x.t.ScheduledWindow, x.From!, x.To!));
        return ServiceReach.For(trip.ScheduledWindow, origin, destination, homeLocationId is int home ? At(home) : null, held);
    }

    private GeoCoordinate? At(int locationId) => _locations.Find(locationId)?.Coordinate;
}
