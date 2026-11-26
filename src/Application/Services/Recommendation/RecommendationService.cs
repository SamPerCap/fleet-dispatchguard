using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Recommendation;

/// <summary>Only reads: applying is one <see cref="DriverAllocationService.AllocateDriver"/> per line (SPEC 0062).</summary>
public interface IRecommendationService
{
    Task<RecommendationDto> ProposeAsync(RecommendationRequest request, CancellationToken ct = default);

    /// <summary>In order; a line that no longer passes does not count for the next ones (SPEC 0062 R9).</summary>
    Task<IReadOnlyList<RecommendationLineDto>> CheckAsync(RecommendationCheckRequest request, CancellationToken ct = default);

    Task<RecommendationOptionsDto> OptionsAsync(RecommendationOptionsRequest request, CancellationToken ct = default);

    /// <summary>For the badge of the manual panel.</summary>
    Task<TripRecommendationDto> ForTripAsync(int tripId, CancellationToken ct = default);
}

public sealed class RecommendationService : IRecommendationService
{
    public const int MaxTrips = 100;

    /// <summary>From the first departure, inclusive.</summary>
    public const int MaxDays = 31;

    public static readonly LocalizedText Note = Texts.RecommendationNote();

    /// <summary>Arriving is an estimate, never a rule (SPEC 0062 R6).</summary>
    public static readonly LocalizedText ArrivalNote = Texts.RecommendationArrivalNote(TravelAssumptions.AverageSpeedKmh);

    private readonly IPlanningSnapshotSource _source;
    private readonly IVehicleEligibilityService _eligibility;
    private readonly TimeProvider _clock;

    public RecommendationService(IPlanningSnapshotSource source, IVehicleEligibilityService eligibility, TimeProvider clock)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Nearest departures within the limits; then bookings by their first pending departure, each in time order (SPEC 0062 R2, R3).</summary>
    public static (IReadOnlyList<Trip> Trips, int LeftOut) Select(IReadOnlyCollection<Trip> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Count == 0) return ([], 0);
        var byDeparture = pending.OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToList();
        var limit = byDeparture[0].ScheduledWindow.Start.AddDays(MaxDays);
        var selected = byDeparture.TakeWhile(t => t.ScheduledWindow.Start <= limit).Take(MaxTrips).ToList();
        var ordered = selected.GroupBy(t => t.BookingId)
            .OrderBy(g => g.Min(t => t.ScheduledWindow.Start)).ThenBy(g => g.Key)
            .SelectMany(g => g.OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id))
            .ToList();
        return (ordered, pending.Count - selected.Count);
    }

    public async Task<RecommendationDto> ProposeAsync(RecommendationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BookingId is null && (request.From is null || request.To is null))
        {
            throw new InvalidRequestException(Texts.BookingOrPeriodNeeded());
        }

        if (request.From is { } from && request.To is { } to && to <= from) throw new InvalidRequestException(Texts.FieldAfter(nameof(request.To), nameof(request.From)));
        if (request.BookingId is int booking && !await _source.BookingExistsAsync(booking, ct)) throw MissingResource.Booking(booking);
        if (request.BaseId is int operatingBase && !await _source.BaseExistsAsync(operatingBase, ct)) throw MissingResource.Base(operatingBase);

        var now = Now;
        var pending = await _source.PendingAsync(now, request.BookingId, request.BaseId, request.From, request.To, ct);
        var (selected, leftOut) = Select(pending);
        var lines = new List<RecommendationLineDto>(selected.Count);
        if (selected.Count > 0)
        {
            var (snapshot, simulation) = await SimulateAsync(selected, ct);
            foreach (var id in selected.Select(s => s.Id))
            {
                // The copy is a later read: what changed in between is left as it is.
                if (await simulation.TripAsync(id) is not { } trip || trip.WhyNotRecommendable(now) is not null) continue;
                lines.Add(await ProposeLineAsync(simulation, snapshot, trip, ct));
            }
        }

        return new RecommendationDto(now, lines, leftOut, MaxTrips, MaxDays, RecommendationSteps.Km, Note, ArrivalNote);
    }

    public async Task<IReadOnlyList<RecommendationLineDto>> CheckAsync(RecommendationCheckRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = Validated(request.Lines);
        var (snapshot, simulation, trips) = await LoadLinesAsync(inputs, ct);
        var now = Now;
        var lines = new List<RecommendationLineDto>(inputs.Count);
        foreach (var input in inputs)
        {
            lines.Add(await CheckLineAsync(simulation, snapshot, trips[input.TripId], input, now, ct));
        }

        return lines;
    }

    public async Task<RecommendationOptionsDto> OptionsAsync(RecommendationOptionsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = Validated(request.Lines);
        if (inputs.All(i => i.TripId != request.TripId)) inputs = [.. inputs, new RecommendationLineInput(request.TripId, null, null)];
        var (snapshot, simulation, trips) = await LoadLinesAsync(inputs, ct);
        var trip = trips[request.TripId];
        var now = Now;
        if (trip.WhyNotRecommendable(now) is not null) return new RecommendationOptionsDto([], []);

        foreach (var input in inputs.Where(i => i.TripId != request.TripId))
        {
            await CheckLineAsync(simulation, snapshot, trips[input.TripId], input, now, ct);
        }

        var candidates = await simulation.CandidatesAsync(trip, ct);
        return new RecommendationOptionsDto(
            candidates.Drivers.Select(d => Resource(simulation, d.Candidate.Driver, d.Reach)).ToList(),
            candidates.Vehicles.Select(v => Resource(simulation, v.Vehicle, v.Reach)).ToList());
    }

    public async Task<TripRecommendationDto> ForTripAsync(int tripId, CancellationToken ct = default)
    {
        var trips = await _source.TripsAsync([tripId], ct);
        if (trips.Count == 0) throw MissingResource.Trip(tripId);
        var (_, simulation) = await SimulateAsync(trips, ct);
        var trip = await simulation.TripAsync(tripId) ?? throw MissingResource.Trip(tripId);
        var candidates = await simulation.CandidatesAsync(trip, ct);

        // When the trip already has a vehicle, the panel keeps it: the driver is chosen for that vehicle.
        var vehicles = trip.AssignedVehicleId is int current && simulation.Vehicle(current) is { } vehicle
            ? [new ReachCandidate(current, new ServiceReach(0, ReachSource.PreviousService, null, null, null))]
            : candidates.Vehicles.Select(v => v.Reach).ToList();
        var pick = RecommendationPick.Choose(candidates.Drivers.Select(d => d.Reach).ToList(), vehicles);
        return new TripRecommendationDto(tripId, Now, pick?.Driver.Id, trip.AssignedVehicleId is null ? pick?.Vehicle.Id : null);
    }

    private static async Task<RecommendationLineDto> ProposeLineAsync(RecommendationSimulation simulation, PlanningSnapshot snapshot, Trip trip, CancellationToken ct)
    {
        var candidates = await simulation.CandidatesAsync(trip, ct);
        var pick = candidates.Pick();
        if (pick is null)
        {
            var drivers = ReachCounts.Of(candidates.Drivers.Select(d => d.Reach).ToList());
            var vehicles = ReachCounts.Of(candidates.Vehicles.Select(v => v.Reach).ToList());
            return Line(simulation, snapshot, trip, RecommendationLineStatus.NoSolution, NoSolutionReason(drivers, vehicles), null, null) with
            {
                Drivers = drivers, Vehicles = vehicles
            };
        }

        var driver = simulation.Driver(pick.Driver.Id)!;
        var vehicle = simulation.Vehicle(pick.Vehicle.Id)!;
        var verdict = await simulation.TryAssignAsync(trip, driver, vehicle, ct);
        return Decided(simulation, snapshot, trip, driver, vehicle, pick.Driver, pick.Vehicle, verdict);
    }

    private static async Task<RecommendationLineDto> CheckLineAsync(
        RecommendationSimulation simulation, PlanningSnapshot snapshot, Trip trip, RecommendationLineInput input, DateTime now, CancellationToken ct)
    {
        if (trip.WhyNotRecommendable(now) is { } state) return Line(simulation, snapshot, trip, RecommendationLineStatus.Invalid, state, null, null);
        if (input.DriverId is not int driverId || input.VehicleId is not int vehicleId)
        {
            return Line(simulation, snapshot, trip, RecommendationLineStatus.NoSolution, Texts.NoPairChosen(), null, null);
        }

        var driver = simulation.Driver(driverId) ?? throw MissingResource.Driver(driverId);
        var vehicle = simulation.Vehicle(vehicleId) ?? throw MissingResource.Vehicle(vehicleId);
        var siblings = await simulation.SiblingsAsync(trip, ct);
        var driverReach = simulation.ReachOf(trip, driver, siblings.Any(s => s.AssignedDriverId == driverId));
        var vehicleReach = simulation.ReachOf(trip, vehicle, siblings.Any(s => s.AssignedVehicleId == vehicleId));
        if (!input.Manual && (driverReach.Reach.LateReason is not null || vehicleReach.Reach.LateReason is not null))
        {
            // After another line changed, the proposed pair would no longer be proposed. By hand it can still be chosen (SPEC 0062 R6, R9).
            var late = driverReach.Reach.LateReason is { } lateDriver ? Texts.TheDriver(lateDriver) : Texts.TheVehicle(vehicleReach.Reach.LateReason!);
            return Line(simulation, snapshot, trip, RecommendationLineStatus.Invalid, late,
                Resource(simulation, driver, driverReach), Resource(simulation, vehicle, vehicleReach));
        }

        var verdict = await simulation.TryAssignAsync(trip, driver, vehicle, ct);
        return Decided(simulation, snapshot, trip, driver, vehicle, driverReach, vehicleReach, verdict);
    }

    private static RecommendationLineDto Decided(
        RecommendationSimulation simulation, PlanningSnapshot snapshot, Trip trip, Driver driver, Vehicle vehicle,
        ReachCandidate driverReach, ReachCandidate vehicleReach, AllocationVerdict verdict)
    {
        var explanation = DecisionExplainer.ForProposal(verdict.Trace);
        var reason = verdict.IsAllowed ? explanation.Summary
            : DriverAllocationService.RejectionDetail(explanation) ?? explanation.Summary;
        var warnings = verdict.Trace.Compliance?.ToSnapshot().Warnings.ToList() ?? [];
        return Line(simulation, snapshot, trip, verdict.IsAllowed ? RecommendationLineStatus.Proposed : RecommendationLineStatus.Invalid, reason,
            Resource(simulation, driver, driverReach), Resource(simulation, vehicle, vehicleReach), warnings, explanation);
    }

    private static RecommendationLineDto Line(
        RecommendationSimulation simulation, PlanningSnapshot snapshot, Trip trip, RecommendationLineStatus status, LocalizedText reason,
        RecommendedResourceDto? driver, RecommendedResourceDto? vehicle, IReadOnlyList<LocalizedText>? warnings = null, DecisionExplanation? explanation = null) => new(
            trip.Id, trip.BookingId, snapshot.CustomerByBooking.GetValueOrDefault(trip.BookingId, string.Empty), trip.BaseId,
            trip.ScheduledWindow.Start, trip.ScheduledWindow.End, simulation.LocationName(trip.OriginLocationId),
            simulation.LocationName(trip.DestinationLocationId), trip.RequiredSeats, status, driver, vehicle, reason, warnings ?? [], explanation);

    private static RecommendedResourceDto Resource(RecommendationSimulation simulation, Driver driver, ReachCandidate reach) =>
        new(driver.Id, Texts.AsWritten(driver.FullName), Km(reach), reach.Reach.StepKm, Where(simulation, reach.Reach, driver.HomeLocationId), reach.SameBooking, reach.Reach.LateReason);

    private static RecommendedResourceDto Resource(RecommendationSimulation simulation, Vehicle vehicle, ReachCandidate reach) =>
        new(vehicle.Id, Texts.PlateAndSeats(vehicle.LicensePlate, vehicle.SeatingCapacity), Km(reach), reach.Reach.StepKm,
            Where(simulation, reach.Reach, vehicle.HomeLocationId), reach.SameBooking, reach.Reach.LateReason);

    private static double? Km(ReachCandidate reach) => reach.Reach.DistanceKm is double km ? Math.Round(km, 1) : null;

    private static LocalizedText Where(RecommendationSimulation simulation, ServiceReach reach, int? homeLocationId) => reach.Source switch
    {
        ReachSource.PreviousService => Texts.FromPreviousService(reach.FromTripId!.Value),
        ReachSource.HomeLocation => Texts.FromLocation(simulation.LocationName(homeLocationId!.Value)),
        _ => Texts.UnknownPosition()
    };

    /// <summary>What is missing, and how many valid ones there are farther away or that do not arrive (SPEC 0062 R5).</summary>
    private static LocalizedText NoSolutionReason(ReachCounts drivers, ReachCounts vehicles)
    {
        if (drivers.Valid == 0 && vehicles.Valid == 0) return Texts.NoDriverNorVehicle();
        if (drivers.Valid == 0) return Texts.NoLegalDriver();
        if (vehicles.Valid == 0) return Texts.NoFitVehicle();
        var reasons = new List<LocalizedText> { Texts.FartherAway(RecommendationSteps.Km[^1], drivers.Farther, vehicles.Farther) };
        if (drivers.Late + vehicles.Late > 0)
        {
            reasons.Add(Texts.DoNotArrive(drivers.Late, vehicles.Late));
        }

        if (drivers.UnknownPosition + vehicles.UnknownPosition > 0)
        {
            reasons.Add(Texts.WithoutPosition(drivers.UnknownPosition, vehicles.UnknownPosition));
        }

        return Texts.Sentences(reasons);
    }

    private static IReadOnlyList<RecommendationLineInput> Validated(IReadOnlyList<RecommendationLineInput>? lines)
    {
        if (lines is null || lines.Count == 0) return [];
        if (lines.Count > MaxTrips) throw new InvalidRequestException(Texts.TooManyLines(MaxTrips));
        if (lines.Any(l => l is null)) throw new InvalidRequestException(Texts.EmptyLine());
        if (lines.Select(l => l.TripId).Distinct().Count() != lines.Count) throw new InvalidRequestException(Texts.RepeatedServices());
        return lines;
    }

    private async Task<(PlanningSnapshot, RecommendationSimulation, Dictionary<int, Trip>)> LoadLinesAsync(IReadOnlyList<RecommendationLineInput> inputs, CancellationToken ct)
    {
        var ids = inputs.Select(i => i.TripId).ToList();
        var found = await _source.TripsAsync(ids, ct);
        var missing = ids.Except(found.Select(t => t.Id)).ToList();
        if (missing.Count > 0) throw MissingResource.Trip(missing[0]);
        if (found.Count > 0 && found.Max(t => t.ScheduledWindow.Start) - found.Min(t => t.ScheduledWindow.Start) > TimeSpan.FromDays(MaxDays))
        {
            throw new InvalidRequestException(Texts.ProposalTooLong(MaxDays));
        }

        if (found.Count == 0) return (new PlanningSnapshot([], [], [], [], new Dictionary<int, string>()), null!, []);

        var (snapshot, simulation) = await SimulateAsync(found, ct);
        var trips = new Dictionary<int, Trip>();
        foreach (var id in ids) trips[id] = (await simulation.TripAsync(id))!;
        return (snapshot, simulation, trips);
    }

    /// <summary>Copies everything any rule, search or position looks at for these trips.</summary>
    private async Task<(PlanningSnapshot Snapshot, RecommendationSimulation Simulation)> SimulateAsync(IReadOnlyCollection<Trip> trips, CancellationToken ct)
    {
        var calendars = trips.Select(t => LegalValidationService.CalendarFor(t.ScheduledWindow)).ToList();
        var from = calendars.Min(c => c.From) - VehicleCandidateSearch.PositionLookback;
        var snapshot = await _source.LoadAsync(from, calendars.Max(c => c.To), trips.Select(t => t.BookingId).Distinct().ToList(), ct);
        return (snapshot, new RecommendationSimulation(snapshot, _eligibility));
    }
}
