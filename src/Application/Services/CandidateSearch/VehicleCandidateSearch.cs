using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.CandidateSearch;

/// <summary>Only filters and ranks; assigning stays in <see cref="DriverAllocationService"/>.</summary>
public interface IVehicleCandidateSearch
{
    /// <summary>
    /// Hard filters: auto-assignable and not external, not the vehicle currently assigned, eligible per
    /// <see cref="IVehicleEligibilityService"/> (active, capacity, unavailability) and no overlapping
    /// non-cancelled trip. Ranked by distance from the vehicle's position (last trip destination → home
    /// base → unknown, last). Returns an empty list (never throws) when nothing qualifies (SPEC 0032).
    /// </summary>
    Task<IReadOnlyList<VehicleCandidate>> FindCandidates(Trip trip, int maxResults = VehicleCandidateSearch.DefaultMaxResults, CancellationToken cancellationToken = default);

    /// <exception cref="KeyNotFoundException">Unknown trip.</exception>
    Task<IReadOnlyList<VehicleCandidate>> FindCandidatesForTrip(int tripId, int maxResults = VehicleCandidateSearch.DefaultMaxResults, CancellationToken cancellationToken = default);
}

public sealed class VehicleCandidateSearch : IVehicleCandidateSearch
{
    /// <summary>docs/PROJECT_DEFINITION.md §4.</summary>
    public const int DefaultMaxResults = DriverCandidateSearch.DefaultMaxResults;

    /// <summary>
    /// How far back a previous trip still defines where the vehicle is. Older than this, the home base is used.
    /// Operational approximation (not regulatory): a vehicle idle for a week is assumed back at its depot.
    /// </summary>
    public static readonly TimeSpan PositionLookback = TimeSpan.FromDays(7);

    private const int MaxTripsInLookback = 500;

    private readonly IVehicleRepository _vehicleRepository;
    private readonly ITripRepository _tripRepository;
    private readonly IVehicleEligibilityService _eligibilityService;
    private readonly ILocationRepository _locationRepository;

    public VehicleCandidateSearch(
        IVehicleRepository vehicleRepository,
        ITripRepository tripRepository,
        IVehicleEligibilityService eligibilityService,
        ILocationRepository locationRepository)
    {
        _vehicleRepository = vehicleRepository ?? throw new ArgumentNullException(nameof(vehicleRepository));
        _tripRepository = tripRepository ?? throw new ArgumentNullException(nameof(tripRepository));
        _eligibilityService = eligibilityService ?? throw new ArgumentNullException(nameof(eligibilityService));
        _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
    }

    public async Task<IReadOnlyList<VehicleCandidate>> FindCandidatesForTrip(int tripId, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken)
            ?? throw MissingResource.Trip(tripId);
        return await FindCandidates(trip, maxResults, cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleCandidate>> FindCandidates(Trip trip, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);

        var window = trip.ScheduledWindow;
        var locations = new Dictionary<int, Location?>();
        var origin = await ResolveLocation(trip.OriginLocationId, trip.OriginLocation, locations, cancellationToken)
            ?? throw new InvalidOperationException($"Trip {trip.Id} has no origin location");

        var vehicles = (await _vehicleRepository.GetAllAsync(cancellationToken))
            .Where(v => v.IsEligibleForAutoAssignment()
                && v.Id != trip.AssignedVehicleId
                && _eligibilityService.CheckEligibility(v, window, trip.RequiredSeats).IsEligible)
            .ToList();

        var ranked = new List<(Vehicle Vehicle, VehicleCandidate Draft)>(vehicles.Count);
        foreach (var vehicle in vehicles)
        {
            var trips = await RecentTrips(vehicle.Id, trip, cancellationToken);
            if (trips.Any(t => t.ScheduledWindow.OverlapsWith(window)))
            {
                continue;
            }

            var lastTrip = trips
                .Where(t => t.ScheduledWindow.End <= window.Start)
                .OrderByDescending(t => t.ScheduledWindow.End)
                .ThenByDescending(t => t.Id)
                .FirstOrDefault();

            ranked.Add((vehicle, await Rank(vehicle, lastTrip, trip, origin, locations, cancellationToken)));
        }

        return ranked
            .OrderBy(r => r.Draft.Proximity)
            .ThenBy(r => r.Draft.DistanceKm ?? double.MaxValue)
            .ThenBy(r => r.Vehicle.Id) // Deterministic tie-break.
            .Take(maxResults)
            .Select((r, i) => new VehicleCandidate(r.Vehicle, i + 1, r.Draft.Proximity, r.Draft.PositionSource, r.Draft.DistanceKm, r.Draft.Reason, trip.RequiredSeats))
            .ToList();
    }

    private async Task<VehicleCandidate> Rank(Vehicle vehicle, Trip? lastTrip, Trip trip, Location origin, Dictionary<int, Location?> cache, CancellationToken ct)
    {
        Location? position = null;
        var source = VehiclePositionSource.Unknown;
        if (lastTrip is not null)
        {
            position = await ResolveLocation(lastTrip.DestinationLocationId, lastTrip.DestinationLocation, cache, ct);
            source = VehiclePositionSource.LastTripDestination;
        }

        if (position is null && vehicle.HomeLocationId is int homeId)
        {
            position = await ResolveLocation(homeId, vehicle.HomeLocation, cache, ct);
            source = VehiclePositionSource.HomeBase;
        }

        if (position is null)
        {
            return new VehicleCandidate(vehicle, 1, CandidateProximity.NoHomeLocation, VehiclePositionSource.Unknown, null,
                Texts.VehicleUnknownReason());
        }

        var where = source == VehiclePositionSource.LastTripDestination
            ? Texts.VehicleFromLastTrip(lastTrip!.Id, position.Name)
            : Texts.VehicleFromBase(position.Name);
        var distance = position.Coordinate.DistanceTo(origin.Coordinate);

        if (position.Id == origin.Id)
        {
            return vehicle.HomeLocationId == trip.DestinationLocationId
                ? new VehicleCandidate(vehicle, 1, CandidateProximity.BaseAtOriginAndDestination, source, distance, Texts.VehicleIsBothReason(where))
                : new VehicleCandidate(vehicle, 1, CandidateProximity.BaseAtOrigin, source, distance, Texts.VehicleIsOriginReason(where));
        }


        foreach (var (maxKm, proximity) in ProximityRankingPolicy.RadiusBands)
        {
            if (distance <= maxKm)
            {
                return new VehicleCandidate(vehicle, 1, proximity, source, distance, Texts.VehicleWithinReason(where, distance, maxKm));
            }
        }

        return new VehicleCandidate(vehicle, 1, CandidateProximity.Beyond40Km, source, distance, Texts.VehicleBeyondReason(where, distance, ProximityRankingPolicy.RadiusBands[^1].MaxKm));
    }

    /// <summary>Other non-cancelled trips of the vehicle, from the lookback up to the trip's end.</summary>
    private async Task<List<Trip>> RecentTrips(int vehicleId, Trip trip, CancellationToken cancellationToken)
    {
        var window = trip.ScheduledWindow;
        var page = await _tripRepository.GetByVehicleAsync(vehicleId, window.Start - PositionLookback, window.End, 1, MaxTripsInLookback, trip.Id, cancellationToken);
        return page.Items.Where(t => t.Status != TripStatus.Cancelled).ToList();
    }

    private async Task<Location?> ResolveLocation(int id, Location? loaded, Dictionary<int, Location?> cache, CancellationToken cancellationToken)
    {
        if (loaded is not null)
        {
            return loaded;
        }

        if (!cache.TryGetValue(id, out var location))
        {
            location = await _locationRepository.GetByIdAsync(id, cancellationToken);
            cache[id] = location;
        }

        return location;
    }
}
