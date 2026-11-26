using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Optimization;

public sealed record VehicleChange(Trip Trip, Vehicle Current, Vehicle Proposed, DecisionExplanation Explanation);

public sealed record ChainingPlan(
    int TripsConsidered,
    int TripsDeparted,
    double BaselineDeadheadKm,
    double ProposedDeadheadKm,
    IReadOnlyList<VehicleChange> Changes,
    DecisionExplanation Explanation)
{
    /// <summary>0 when the baseline has no empty km.</summary>
    public double SavingPercent => BaselineDeadheadKm <= 0 ? 0 : (BaselineDeadheadKm - ProposedDeadheadKm) / BaselineDeadheadKm * 100;
}

/// <summary>
/// Greedy heuristic, not a global optimum: trips are taken by departure and each gets the closest vehicle that is eligible and
/// arrives in time (<see cref="TravelAssumptions"/>) (SPEC 0043 R2–R6). Trips that have already departed are facts: they keep
/// their vehicle unconditionally and still count for its position and for the km, before and after (SPEC 0070).
/// Nobody leaves before now, and the estimate never rules out the vehicle a trip already has (SPEC 0071). Days are Madrid's.
/// </summary>
public static class VehicleChainingPlanner
{
    private const string Adr = "docs/adr/0013-optimizador-heuristico-sincrono.md";
    private const double Epsilon = 1e-9;

    /// <param name="now">UTC instant: trips that start at it or before have departed (<see cref="Trip.HasDeparted"/>).</param>
    /// <param name="lastServices">Last departed service of each vehicle, in the plan or not: where it really is (SPEC 0071 R2). Only
    /// decides whether it arrives; the km still start at the base (SPEC 0043 R4).</param>
    public static ChainingPlan Plan(
        IReadOnlyList<Trip> trips,
        IReadOnlyList<Vehicle> fleet,
        IReadOnlyDictionary<int, Location> locations,
        bool returnToBase,
        IVehicleEligibilityService eligibility,
        DateTime now,
        IReadOnlyList<Trip>? lastServices = null)
    {
        ArgumentNullException.ThrowIfNull(trips);
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(eligibility);
        if (now.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("'now' must be a UTC instant", nameof(now));
        }

        var ordered = trips
            .Where(t => t.IsActive && t.AssignedVehicleId is not null && t.AssignedDriverId is not null)
            .OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id)
            .ToList();
        var vehicles = fleet.ToDictionary(v => v.Id);
        var current = ordered.ToDictionary(t => t.Id, t => t.AssignedVehicleId!.Value);
        var missing = current.Values.FirstOrDefault(id => !vehicles.ContainsKey(id), -1);
        if (missing != -1)
        {
            throw new InvalidOperationException($"Vehicle {missing} not loaded");
        }

        var baseline = DeadheadKm(ordered, current, vehicles, locations, returnToBase);
        var departed = ordered.Count(t => t.HasDeparted(now));
        if (departed > 0 && departed == ordered.Count)
        {
            return Unchanged(ordered.Count, departed, baseline, "SPEC 0070 R3",
                departed == 1 ? Texts.OnlyServiceDeparted() : Texts.EveryServiceDeparted(departed));
        }

        // A trip nobody else reaches keeps its vehicle: if an earlier proposal had taken it, that one is undone by reserving it (SPEC 0071 R3).
        var known = (lastServices ?? []).Where(t => t.IsActive && t.AssignedVehicleId is not null && t.HasDeparted(now))
            .GroupBy(t => t.AssignedVehicleId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(t => t.ScheduledWindow.Start)!);
        var run = new Run(ordered, vehicles, current, known, locations, returnToBase, eligibility, now);
        var reserved = new HashSet<int>();
        Outcome result;
        while ((result = run.Assign(reserved)).Unserved is Trip unserved)
        {
            if (!result.CanKeep || !reserved.Add(unserved.Id))
            {
                return Unchanged(ordered.Count, departed, baseline, "SPEC 0043 R6",
                    Texts.NoVehicleArrives(unserved.Id));
            }
        }

        var (proposed, reasons) = (result.Proposed, result.Reasons);
        var after = DeadheadKm(ordered, proposed, vehicles, locations, returnToBase);
        if (after >= baseline - Epsilon)
        {
            return Unchanged(ordered.Count, departed, baseline, "SPEC 0043 R6",
                Texts.NoBetterPlan(baseline));
        }

        var changes = ordered
            .Where(t => proposed[t.Id] != current[t.Id])
            .Select(t => new VehicleChange(t, vehicles[current[t.Id]], vehicles[proposed[t.Id]],
                Explain(t, vehicles[proposed[t.Id]], reasons[t.Id], locations, now)))
            .ToList();
        var plan = new ChainingPlan(ordered.Count, departed, baseline, after, changes, Unused);
        return plan with { Explanation = Summary(plan, returnToBase, result, now) };
    }

    /// <summary>
    /// Per vehicle: base → first origin, each destination → next origin and, with <paramref name="returnToBase"/>, every day starts
    /// and ends at the base. <paramref name="ordered"/> must be sorted by departure.
    /// </summary>
    public static double DeadheadKm(
        IReadOnlyList<Trip> ordered, IReadOnlyDictionary<int, int> vehicleByTrip,
        IReadOnlyDictionary<int, Vehicle> vehicles, IReadOnlyDictionary<int, Location> locations, bool returnToBase)
    {
        double total = 0;
        foreach (var group in ordered.GroupBy(t => vehicleByTrip[t.Id]))
        {
            var home = Home(vehicles[group.Key], locations);
            var position = home;
            DateOnly? day = null;
            foreach (var trip in group)
            {
                var tripDay = DayOf(trip);
                if (returnToBase && day is not null && tripDay != day)
                {
                    total += Dist(position, home);
                    position = home;
                }

                total += Dist(position, Loc(locations, trip.OriginLocationId));
                position = Loc(locations, trip.DestinationLocationId);
                day = tripDay;
            }

            if (returnToBase)
            {
                total += Dist(position, home);
            }
        }

        return total;
    }

    private static readonly DecisionExplanation Unused = new(Texts.AsWritten(string.Empty), [], [], [], []);

    private sealed record VehicleState(Location? Position, DateTime FreeAt, DateOnly? Day, Trip? LastTrip);

    /// <summary>Why a vehicle was chosen: the position its km count from, and where and when it really leaves from.</summary>
    private sealed record Reason(VehicleState State, double Km, VehicleState From, DateTime Departs, TimeSpan Travel)
    {
        public bool Arrives(Trip trip) => Departs + Travel <= trip.ScheduledWindow.Start;
    }

    /// <summary>
    /// <c>Late</c> and <c>Unfit</c>: trips that keep their vehicle though it does not arrive, or is not fit and nobody else arrives.
    /// <c>Unserved</c>: the trip that nobody can do, and whether reserving its vehicle would solve it (<c>CanKeep</c>).
    /// </summary>
    private sealed record Outcome(Dictionary<int, int> Proposed, Dictionary<int, Reason> Reasons, List<int> Late, List<int> Unfit,
        Trip? Unserved = null, bool CanKeep = false);

    /// <summary>One pass of the heuristic; pure, so that it can be repeated with more vehicles reserved.</summary>
    private sealed class Run(
        IReadOnlyList<Trip> ordered, IReadOnlyDictionary<int, Vehicle> vehicles, IReadOnlyDictionary<int, int> current,
        IReadOnlyDictionary<int, Trip> known, IReadOnlyDictionary<int, Location> locations, bool returnToBase,
        IVehicleEligibilityService eligibility, DateTime now)
    {
        private readonly Dictionary<int, DateOnly> _days = ordered.ToDictionary(t => t.Id, DayOf);

        private bool IsEligible(Vehicle vehicle, Trip trip) => eligibility.CheckEligibility(vehicle, trip.ScheduledWindow, trip.RequiredSeats).IsEligible;

        public Outcome Assign(IReadOnlySet<int> reserved)
        {
            var proposed = new Dictionary<int, int>();
            var states = new Dictionary<int, VehicleState>();
            var facts = known.ToDictionary(k => k.Key, k => After(k.Value, locations, Home(vehicles, k.Key, locations)));
            var reasons = new Dictionary<int, Reason>();
            var late = new List<int>();
            var unfit = new List<int>();
            var reservedOf = ordered.Where(t => reserved.Contains(t.Id)).ToLookup(t => current[t.Id]);
            foreach (var trip in ordered)
            {
                var currentId = current[trip.Id];
                if (trip.HasDeparted(now))
                {
                    // Departed trips come first (order by start): whatever the model thinks of them, this is where the vehicle is.
                    proposed[trip.Id] = currentId;
                    states[currentId] = After(trip, locations);
                    if (!facts.TryGetValue(currentId, out var fact) || fact.LastTrip!.ScheduledWindow.Start <= trip.ScheduledWindow.Start)
                    {
                        facts[currentId] = states[currentId];
                    }

                    continue;
                }

                var origin = Loc(locations, trip.OriginLocationId);
                Vehicle? chosen = null;
                Reason? best = null, lastResort = null;
                var currentFits = false;
                var onlyByEstimate = reserved.Contains(trip.Id);
                var candidates = trip.IsLocked
                    ? vehicles.Values.Where(v => v.Id == currentId)
                    : vehicles.Values.Where(v => v.Id == currentId || v.IsEligibleForAutoAssignment());

                // Current vehicle first, then by id: ties keep the current assignment (SPEC 0043 R3).
                foreach (var vehicle in candidates.OrderBy(v => v.Id != currentId).ThenBy(v => v.Id))
                {
                    if (!IsEligible(vehicle, trip))
                    {
                        continue;
                    }

                    var (reason, first) = ReasonFor(vehicle, trip, origin, states, facts);
                    if (vehicle.Id == currentId)
                    {
                        currentFits = true;
                        if (!first && !reason.Arrives(trip))
                        {
                            lastResort = OwnPrevious(vehicle, states) ? reason : null;
                            continue;
                        }
                    }
                    else if (!reason.Arrives(trip)) // Also rules out overlaps.
                    {
                        onlyByEstimate |= reason.State.FreeAt + TravelAssumptions.TravelTime(reason.Km) <= trip.ScheduledWindow.Start;
                        continue;
                    }
                    else if (BlocksReserved(vehicle, trip, reservedOf[vehicle.Id]))
                    {
                        onlyByEstimate = true;
                        continue;
                    }

                    if (best is null || reason.Km < best.Km - Epsilon)
                    {
                        chosen = vehicle;
                        best = reason;
                    }
                }

                if (chosen is null && !currentFits && onlyByEstimate)
                {
                    // Its vehicle is not fit, but it was the estimate that ruled the others out: that does not invalidate the report either.
                    var own = vehicles[currentId];
                    var (reason, first) = ReasonFor(own, trip, origin, states, facts);
                    lastResort = first || reason.Arrives(trip) || OwnPrevious(own, states) ? reason : null;
                }

                if (chosen is null && lastResort is not null)
                {
                    (chosen, best) = (vehicles[currentId], lastResort);
                }

                if (chosen is null)
                {
                    return new Outcome(proposed, reasons, late, unfit, trip, currentFits || onlyByEstimate);
                }

                if (chosen.Id == currentId && !currentFits)
                {
                    unfit.Add(trip.Id);
                }
                else if (!best!.Arrives(trip))
                {
                    late.Add(trip.Id);
                }

                proposed[trip.Id] = chosen.Id;
                reasons[trip.Id] = best!;
                states[chosen.Id] = After(trip, locations);
            }

            return new Outcome(proposed, reasons, late, unfit);
        }

        /// <summary><c>First</c>: it is its first service from now on.</summary>
        private (Reason Reason, bool First) ReasonFor(Vehicle vehicle, Trip trip, Location origin, Dictionary<int, VehicleState> states, Dictionary<int, VehicleState> facts)
        {
            var home = Home(vehicle, locations);
            var planned = states.GetValueOrDefault(vehicle.Id);
            var state = StateAt(home, planned, trip);

            // Its first service from now on: it is where its last departed one left it, in the plan or not (SPEC 0071 R2).
            var first = planned is null || planned.LastTrip!.HasDeparted(now);
            var from = first && facts.TryGetValue(vehicle.Id, out var fact) ? StateAt(home, fact, trip) : state;
            return (new Reason(state, Dist(state.Position, origin), from, from.FreeAt > now ? from.FreeAt : now,
                TravelAssumptions.TravelTime(Dist(from.Position, origin))), first);
        }

        /// <summary>The service it does before is already its own today: if it is late, no proposal caused it.</summary>
        private bool OwnPrevious(Vehicle vehicle, Dictionary<int, VehicleState> states) =>
            states.TryGetValue(vehicle.Id, out var planned) && current[planned.LastTrip!.Id] == vehicle.Id;

        /// <summary>With this trip, the vehicle would not reach a later one of its own that nobody else can do.</summary>
        private bool BlocksReserved(Vehicle vehicle, Trip trip, IEnumerable<Trip> reservedOfVehicle) =>
            reservedOfVehicle.Any(t =>
            {
                if (IsBefore(t, trip))
                {
                    return false;
                }

                var state = StateAt(Home(vehicle, locations), After(trip, locations), t);
                return state.FreeAt + TravelAssumptions.TravelTime(Dist(state.Position, Loc(locations, t.OriginLocationId))) > t.ScheduledWindow.Start;
            });

        private VehicleState StateAt(Location? home, VehicleState? state, Trip trip)
        {
            if (state is null)
            {
                return new VehicleState(home, DateTime.MinValue, null, null);
            }

            if (returnToBase && state.Day != _days[trip.Id])
            {
                // Back at the base overnight: free once the return leg is done (SPEC 0043 R4).
                return new VehicleState(home, state.FreeAt + TravelAssumptions.TravelTime(Dist(state.Position, home)), null, null);
            }

            return state;
        }
    }

    private static bool IsBefore(Trip one, Trip other) =>
        one.ScheduledWindow.Start < other.ScheduledWindow.Start || (one.ScheduledWindow.Start == other.ScheduledWindow.Start && one.Id < other.Id);

    /// <summary>Madrid day of its departure: the one that returning to base every day goes by (SPEC 0071 R4).</summary>
    private static DateOnly DayOf(Trip trip) => OperationalDay.DateOf(trip.ScheduledWindow.Start);

    private static VehicleState After(Trip trip, IReadOnlyDictionary<int, Location> locations) =>
        new(Loc(locations, trip.DestinationLocationId), trip.ScheduledWindow.End, DayOf(trip), trip);

    /// <summary>A service from outside the plan may end in a location that is no longer active: then it counts from the base.</summary>
    private static VehicleState After(Trip trip, IReadOnlyDictionary<int, Location> locations, Location? home) =>
        new(locations.GetValueOrDefault(trip.DestinationLocationId) ?? home, trip.ScheduledWindow.End, DayOf(trip), trip);

    private static Location? Home(IReadOnlyDictionary<int, Vehicle> vehicles, int id, IReadOnlyDictionary<int, Location> locations) =>
        vehicles.TryGetValue(id, out var vehicle) ? Home(vehicle, locations) : null;

    private static Location? Home(Vehicle vehicle, IReadOnlyDictionary<int, Location> locations) =>
        vehicle.HomeLocationId is int homeId ? locations.GetValueOrDefault(homeId) : null;

    /// <summary>An unknown position (no base, no previous trip) counts as 0 km, as in the vehicle search.</summary>
    private static double Dist(Location? from, Location? to) => from is null || to is null ? 0 : from.Coordinate.DistanceTo(to.Coordinate);

    private static Location Loc(IReadOnlyDictionary<int, Location> locations, int id) =>
        locations.TryGetValue(id, out var location) ? location : throw new InvalidOperationException($"Location {id} not loaded");

    private static ChainingPlan Unchanged(int count, int departed, double baseline, string rule, LocalizedText reason) =>
        new(count, departed, baseline, baseline, [],
            new DecisionExplanation(reason,
                [new(Texts.NoChanges(), rule, ExplanationOutcome.Warning, reason), .. DepartedFact(departed, count)],
                [new(nameof(VehicleChainingPlanner), Texts.GreedyByDepartureRole(), ExplanationOutcome.Warning, Texts.ReturnsCurrentPlan())],
                [Texts.GreedyPattern()], [Adr]));

    private static DecisionExplanation Explain(Trip trip, Vehicle vehicle, Reason reason, IReadOnlyDictionary<int, Location> locations, DateTime now)
    {
        var origin = Loc(locations, trip.OriginLocationId);

        var state = reason.State;
        var known = state.Position is not null;
        var distance = known ? Texts.StraightLineTo(reason.Km, origin.Name) : Texts.UnknownCountsZero();
        var source = (state.Position, state.LastTrip) switch
        {
            (null, _) => Texts.UnknownNoBase(),
            (Location position, Trip prev) when prev.HasDeparted(now) => Texts.FromDepartedTripDestination(prev.Id, position.Name, prev.ScheduledWindow.End),
            (Location position, Trip prev) => Texts.FromTripDestination(prev.Id, position.Name, prev.ScheduledWindow.End),
            (Location position, null) => Texts.FromItsBase(position.Name)
        };
        var arrival = reason.From.Position is null ? Texts.NoTravelEstimate()
            : Texts.Arrival(Departure(reason, now), reason.Travel.TotalMinutes, TravelAssumptions.AverageSpeedKmh,
                Margin(trip.ScheduledWindow.Start - reason.Departs - reason.Travel));
        var positionOutcome = known ? ExplanationOutcome.Passed : ExplanationOutcome.Warning;

        return new DecisionExplanation(
            Texts.VehicleChange(trip.Id, vehicle.LicensePlate, distance),
            [
                new(Texts.VehicleOrigin(), "SPEC 0043 R3", positionOutcome, source),
                new(Texts.Criterion(), "SPEC 0043 R3", ExplanationOutcome.Passed, Texts.ShortestEmptyLeg(distance)),
                new(Texts.ArrivesInTime(), "SPEC 0071 R1", reason.From.Position is null ? ExplanationOutcome.Warning : ExplanationOutcome.Passed, arrival),
                new(Texts.VehicleFit(), "SPEC 0032", ExplanationOutcome.Passed, Texts.PlannedVehicleFit(vehicle.SeatingCapacity, trip.RequiredSeats)),
                new(Texts.DriverRules(), "ADR 0001", ExplanationOutcome.NotEvaluated, Texts.PlanKeepsDriver())
            ],
            [
                new(nameof(VehicleChainingPlanner), Texts.GreedyNearestRole(), ExplanationOutcome.Passed, Texts.PlateAndEmptyKm(vehicle.LicensePlate, reason.Km)),
                new(nameof(IVehicleEligibilityService), Texts.HardEligibilityRole(), ExplanationOutcome.Passed,
                    Texts.AsWritten($"{nameof(VehicleEligibilityCheck.IsEligible)} = true")),
                new($"{nameof(TravelAssumptions)}.{nameof(TravelAssumptions.TravelTime)}", Texts.EstimatedArrivalRole(), ExplanationOutcome.Passed,
                    Texts.Minutes(reason.Travel.TotalMinutes))
            ],
            [Texts.GreedyPattern(), Texts.PurePattern()],
            [Adr, "docs/adr/0001-validacion-legal-antes-de-asignar.md"]);
    }

    /// <summary>It leaves from now only for its first service from now on.</summary>
    private static LocalizedText Departure(Reason reason, DateTime now)
    {
        var place = reason.From.LastTrip is { } last && !ReferenceEquals(last, reason.State.LastTrip)
            ? Texts.FromLastServicePlace(reason.From.Position!.Name, last.Id)
            : Texts.NoPlace();
        return reason.Departs == now ? Texts.LeavesNow(now, place) : Texts.LeavesWhenFree(reason.Departs, place);
    }

    private static LocalizedText Margin(TimeSpan margin) => margin < TimeSpan.FromDays(1) ? Texts.Minutes(margin.TotalMinutes) : Texts.MoreThanADay();

    private static DecisionExplanation Summary(ChainingPlan plan, bool returnToBase, Outcome result, DateTime now) =>
        new(Texts.PlanSummary(Changes(plan), plan.BaselineDeadheadKm, plan.ProposedDeadheadKm, Saving(plan.SavingPercent)),
            [
                new(Texts.EmptyKm(), "SPEC 0043", ExplanationOutcome.Passed,
                    Texts.EmptyKmBeforeAndAfter(plan.BaselineDeadheadKm, plan.ProposedDeadheadKm, plan.TripsConsidered)),
                .. DepartedFact(plan.TripsDeparted, plan.TripsConsidered),
                new(Texts.ArrivalTitle(), "SPEC 0071 R1", ExplanationOutcome.Passed, Texts.NoBusLeavesBefore(now, TravelAssumptions.AverageSpeedKmh)),
                .. KeptFact(Texts.KeepBusThoughLate(), Texts.KeepBusThoughLateDetail, result.Late),
                .. KeptFact(Texts.KeepBusThoughUnfit(), Texts.KeepBusThoughUnfitDetail, result.Unfit),
                new(Texts.BackToBase(), "SPEC 0043 R4", ExplanationOutcome.Passed, returnToBase ? Texts.EveryDayAtBase() : Texts.PositionCarriedOver())
            ],
            [new(nameof(VehicleChainingPlanner), Texts.GreedyNotOptimalRole(), ExplanationOutcome.Passed, Changes(plan))],
            [Texts.GreedyPattern()], [Adr]);

    /// <summary>In percent: the last decimal shown.</summary>
    private const double SmallestSaving = 0.1;

    private const int MaxListedTrips = 10;

    private static LocalizedText Changes(ChainingPlan plan) => Texts.Changes(plan.Changes.Count);

    /// <summary>Only for plans with changes: a saving that rounds to zero is still a saving (SPEC 0070 R6).</summary>
    private static LocalizedText Saving(double percent) =>
        percent < SmallestSaving / 2 ? Texts.LessThanPercent(SmallestSaving) : Texts.Percent(percent);

    /// <summary>Assignments of the dispatcher that the estimate does not question (SPEC 0071 R3).</summary>
    private static ExplanationItem[] KeptFact(LocalizedText title, Func<LocalizedText, LocalizedText> detail, IReadOnlyList<int> tripIds)
    {
        if (tripIds.Count == 0) return [];
        var listed = string.Join(", ", tripIds.Take(MaxListedTrips).Select(id => $"#{id}"));
        var trips = tripIds.Count > MaxListedTrips ? Texts.TripsAndMore(listed, tripIds.Count - MaxListedTrips) : Texts.AsWritten(listed);
        return [new(title, "SPEC 0071 R3", ExplanationOutcome.Warning, detail(trips))];
    }

    /// <summary>What cannot change any more, when there is something (SPEC 0070 R1).</summary>
    private static ExplanationItem[] DepartedFact(int departed, int count) => departed == 0 ? [] :
        [new(Texts.ServicesDeparted(), "SPEC 0070 R1", ExplanationOutcome.Passed, Texts.ServicesDepartedDetail(departed, count))];
}
