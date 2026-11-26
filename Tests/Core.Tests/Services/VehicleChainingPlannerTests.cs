using System.Reflection;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Optimization;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Tests.Services;

/// <summary>Greedy chaining (SPEC 0043); departed services keep their vehicle (SPEC 0070); nobody leaves before now, Madrid days (SPEC 0071).</summary>
public class VehicleChainingPlannerTests
{
    private static readonly DateTime Day = new(2026, 12, 1, 0, 0, 0);
    private static readonly Location PointA = NewLocation(1, 0);
    private static readonly Location PointB = NewLocation(2, 50);   // 50 km north of A
    private static readonly Location PointC = NewLocation(3, 100);  // 100 km north of A
    private static readonly Location PointE = NewLocation(4, 300);  // far away
    private static readonly Driver D1 = NewDriver(1);
    private static readonly Driver D2 = NewDriver(2);
    private static readonly Dictionary<int, Location> Locations = new[] { PointA, PointB, PointC, PointE }.ToDictionary(l => l.Id);

    private static Location NewLocation(int id, double kmNorth)
    {
        var location = new Location($"L{id}", "Calle 1", new GeoCoordinate(40m + (decimal)(kmNorth / 111.195), -3.7m), "Madrid", "Madrid");
        SetId(location, id);
        return location;
    }

    private static Driver NewDriver(int id)
    {
        var driver = new Driver($"ID{id}", "Ana", "Pérez");
        SetId(driver, id);
        return driver;
    }

    private static Vehicle NewVehicle(int id, Location home, int seats = 50, bool auto = true)
    {
        var vehicle = new Vehicle($"P{id}", "Brand", "Model", seats, false, auto);
        SetId(vehicle, id);
        vehicle.SetHomeLocation(home);
        return vehicle;
    }

    private static Trip NewTrip(int id, Location from, Location to, double startHour, double hours, Driver driver, Vehicle vehicle, int dayOffset = 0, bool locked = false)
    {
        var tripStart = Day.AddDays(dayOffset).AddHours(startHour);
        var trip = new Trip(from, to, new TimeWindow(tripStart, tripStart.AddHours(hours)), 10, ServiceType.Regular);
        SetId(trip, id);
        if (locked) trip.MarkAsManuallyAssigned(driver, vehicle); else trip.AssignResources(driver, vehicle);
        return trip;
    }

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);

    /// <summary>Without <paramref name="now"/>, nothing has departed yet.</summary>
    private static ChainingPlan Plan(IEnumerable<Trip> trips, IEnumerable<Vehicle> fleet, bool returnToBase = false, DateTime? now = null, params Trip[] lastServices) =>
        VehicleChainingPlanner.Plan(trips.ToList(), fleet.ToList(), Locations, returnToBase, new VehicleEligibilityService(), now ?? Day.AddDays(-7), lastServices);

    [Fact]
    public void Plan_VehicleEndsWhereNextTripStarts_ChainsAndSavesDeadhead()
    {
        // V1 (base A) does A→B; V2 (base C) does B→A later. Chaining V1 on both avoids the 50 km C→B leg.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v1);
        var t2 = NewTrip(11, PointB, PointA, 12, 1, D2, v2);

        var plan = Plan([t1, t2], [v1, v2]);

        var change = Assert.Single(plan.Changes);
        Assert.Equal(11, change.Trip.Id);
        Assert.Equal(1, change.Proposed.Id);
        Assert.Equal(50, plan.BaselineDeadheadKm, 0);
        Assert.Equal(0, plan.ProposedDeadheadKm, 1);
        Assert.Equal(100, plan.SavingPercent, 0);
        Assert.Contains("#11", change.Explanation.Summary.Es());
        Assert.NotEmpty(change.Explanation.Business);
        Assert.NotEmpty(change.Explanation.Flow);
    }

    [Fact]
    public void Plan_ChangeExplanation_StatesFactsAndDoesNotClaimDriverWasChecked()
    {
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var plan = Plan([NewTrip(10, PointA, PointB, 8, 1, D1, v1), NewTrip(11, PointB, PointA, 12, 1, D2, v2)], [v1, v2]);

        var business = Assert.Single(plan.Changes).Explanation.Business;

        Assert.Equal(ExplanationOutcome.NotEvaluated, Assert.Single(business, i => i.Title.Es().StartsWith("Conductor", StringComparison.Ordinal)).Outcome);
        var origin = Assert.Single(business, i => i.Title.Es() == "Origen del vehículo").Detail;
        Assert.Contains("viaje #10", origin.Es());
        Assert.Contains("0,0 km", Assert.Single(business, i => i.Title.Es() == "Criterio").Detail.Es());
        Assert.Contains("10:00", origin.Es()); // ends 09:00Z = 10:00 in Madrid
        Assert.Contains("180 min", Assert.Single(business, i => i.Title.Es() == "Llega a tiempo").Detail.Es());
        Assert.DoesNotContain(business, i => i.Detail.Es().Contains("porque"));
    }

    [Fact]
    public void Plan_VehicleWithoutKnownPosition_SaysSoInsteadOfADistance()
    {
        // V1 has no base: its distance counts as 0 km, so it is proposed over V2 (50 km away).
        var v1 = new Vehicle("P1", "Brand", "Model", 50, false, true);
        SetId(v1, 1);
        var v2 = NewVehicle(2, PointC);
        var plan = Plan([NewTrip(11, PointB, PointA, 12, 1, D2, v2)], [v1, v2]);

        var business = Assert.Single(plan.Changes).Explanation.Business;
        var origin = Assert.Single(business, i => i.Title.Es() == "Origen del vehículo");

        Assert.Equal(ExplanationOutcome.Warning, origin.Outcome);
        Assert.Contains("desconocida", origin.Detail.Es());
        Assert.DoesNotContain(business, i => i.Detail.Es().Contains("en línea recta"));
    }

    [Fact]
    public void Plan_VehicleCannotArriveInTime_IsNotChained()
    {
        // V1 ends at B at 09:00; next trip leaves C (50 km, ≈43 min at 70 km/h) at 09:30 → too late.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v1);
        var t2 = NewTrip(11, PointC, PointA, 9.5, 1, D2, v2);

        var plan = Plan([t1, t2], [v1, v2]);

        Assert.Empty(plan.Changes);
        Assert.Equal(plan.BaselineDeadheadKm, plan.ProposedDeadheadKm);
    }

    [Fact]
    public void Plan_ArrivalTimeUsesTravelAssumptionsSpeed()
    {
        // V1 ends at A at 09:00; next trip leaves B (50 km ≈ 43 min at 70 km/h) at 09:45 → arrives 09:43 → chained.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointE);
        var t1 = NewTrip(10, PointB, PointA, 8, 1, D1, v1);
        var t2 = NewTrip(11, PointB, PointC, 9.75, 1, D2, v2);

        var plan = Plan([t1, t2], [v1, v2]);

        Assert.True(TravelAssumptions.TravelTime(50) < TimeSpan.FromMinutes(45));
        Assert.Equal(1, Assert.Single(plan.Changes).Proposed.Id);
    }

    [Fact]
    public void Plan_IneligibleVehicle_IsNotProposed()
    {
        var v1 = NewVehicle(1, PointA, seats: 5); // fewer seats than required (10)
        var v2 = NewVehicle(2, PointC);
        var v3 = NewVehicle(3, PointA);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v3);
        var t2 = NewTrip(11, PointA, PointB, 8, 1, D2, v2);

        var plan = Plan([t1, t2], [v1, v2, v3]);

        Assert.DoesNotContain(plan.Changes, c => c.Proposed.Id == 1);
    }

    [Fact]
    public void Plan_LockedTrip_KeepsItsVehicle()
    {
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v2, locked: true);

        var plan = Plan([t1], [v1, v2]);

        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Plan_NonAutoAssignableVehicle_IsNotProposed()
    {
        var v1 = NewVehicle(1, PointA, auto: false);
        var v2 = NewVehicle(2, PointC);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v2);

        var plan = Plan([t1], [v1, v2]);

        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void Plan_ReturnToBase_CountsReturnLegsAndStartsEachDayAtBase()
    {
        // V1 base A: day 1 A→B, day 2 B→A. With return to base: 50 back + 50 out = 100 km; without: 0 km.
        var v1 = NewVehicle(1, PointA);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v1);
        var t2 = NewTrip(11, PointB, PointA, 8, 1, D1, v1, dayOffset: 1);

        var withReturn = Plan([t1, t2], [v1], returnToBase: true);
        var without = Plan([t1, t2], [v1], returnToBase: false);

        Assert.Equal(100, withReturn.BaselineDeadheadKm, 0);
        Assert.Equal(0, without.BaselineDeadheadKm, 1);
    }

    [Fact]
    public void Plan_NoImprovement_ReturnsCurrentPlanWithReason()
    {
        var v1 = NewVehicle(1, PointA);
        var t1 = NewTrip(10, PointA, PointB, 8, 1, D1, v1);

        var plan = Plan([t1], [v1]);

        Assert.Empty(plan.Changes);
        Assert.Equal(ExplanationOutcome.Warning, Assert.Single(plan.Explanation.Business).Outcome);
        Assert.StartsWith("El cálculo no ha encontrado un reparto con menos km en vacío que el actual", plan.Explanation.Summary.Es()); // Not a global optimum (SPEC 0069).
    }

    [Fact]
    public void Plan_NeverProposesOverlappingTripsForOneVehicle()
    {
        // Two simultaneous trips from A: only one can take V1 (base A).
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var v3 = NewVehicle(3, PointC);
        var t1 = NewTrip(10, PointA, PointB, 8, 2, D1, v2);
        var t2 = NewTrip(11, PointA, PointB, 9, 2, D2, v3);

        var plan = Plan([t1, t2], [v1, v2, v3]);

        Assert.Single(plan.Changes, c => c.Proposed.Id == 1);
    }

    [Fact]
    public void Plan_UnassignedOrCancelledTrips_AreIgnored()
    {
        var v1 = NewVehicle(1, PointA);
        var open = new Trip(PointA, PointB, new TimeWindow(Day.AddHours(8), Day.AddHours(9)), 10, ServiceType.Regular);
        SetId(open, 10);
        var cancelled = NewTrip(11, PointA, PointB, 10, 1, D1, v1);
        cancelled.Cancel();

        var plan = Plan([open, cancelled], [v1]);

        Assert.Equal(0, plan.TripsConsidered);
    }

    /// <summary>V1 (base A) does A→B at 08:00 and V2 (base C) B→A at 12:00: the planner moves #11 to V1 and saves 50 km.</summary>
    private static (Trip[] Trips, Vehicle[] Fleet) Chainable()
    {
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        return ([NewTrip(10, PointA, PointB, 8, 1, D1, v1), NewTrip(11, PointB, PointA, 12, 1, D2, v2)], [v1, v2]);
    }

    [Theory]
    [InlineData(0, true)]    // leaves right now: already departed
    [InlineData(-1, false)]  // one tick before leaving: can still change
    [InlineData(1, true)]
    public void Plan_DepartureLimit_OnlyProposesWhatHasNotDeparted(long ticks, bool departed)
    {
        var (trips, fleet) = Chainable();

        var plan = Plan(trips, fleet, now: trips[1].ScheduledWindow.Start.AddTicks(ticks));

        Assert.Equal(departed ? 0 : 1, plan.Changes.Count);
        Assert.Equal(departed ? 2 : 1, plan.TripsDeparted);
        Assert.Equal(50, plan.BaselineDeadheadKm, 0);
        Assert.Equal(departed ? 50 : 0, plan.ProposedDeadheadKm, 0);
    }

    [Fact]
    public void Plan_InProgress_KeepsItsVehicle_AndItsPositionIsUsedForTheNextTrip()
    {
        // #10 is running with V2 (base C, 100 km away; V1 would be at 0 km). It stays with V2 and, as V2 ends at B, it takes #11 from V1.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        Trip[] trips = [NewTrip(10, PointA, PointB, 8, 1, D1, v2), NewTrip(11, PointB, PointA, 12, 1, D2, v1)];

        var free = Plan(trips, [v1, v2]);
        var plan = Plan(trips, [v1, v2], now: Day.AddHours(8.5));

        Assert.Contains(free.Changes, c => c.Trip.Id == 10); // not departed: it would change
        var change = Assert.Single(plan.Changes);
        Assert.Equal((11, 2), (change.Trip.Id, change.Proposed.Id));
        Assert.Equal((2, 1), (plan.TripsConsidered, plan.TripsDeparted));
        Assert.Equal(150, plan.BaselineDeadheadKm, 0); // C→A (departed) + A→B
        Assert.Equal(100, plan.ProposedDeadheadKm, 0); // C→A cannot be saved: it counts in both
        Assert.Contains("ya ha salido", Assert.Single(change.Explanation.Business, i => i.Title.Es() == "Origen del vehículo").Detail.Es());
        Assert.Contains("1 de 2", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Servicios que ya han salido").Detail.Es());
    }

    [Fact]
    public void Plan_DepartedTrip_KeepsItsVehicleEvenIfItIsNotEligibleNow()
    {
        // V2 is not eligible for #10 any more (too few seats here): a fact is not questioned, and the rest is still proposed.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC, seats: 5);
        var v3 = NewVehicle(3, PointC);
        Trip[] trips = [NewTrip(10, PointB, PointC, 8, 1, D1, v2), NewTrip(11, PointA, PointB, 12, 1, D2, v3)];

        var plan = Plan(trips, [v1, v2, v3], now: Day.AddHours(8));

        var change = Assert.Single(plan.Changes);
        Assert.Equal((11, 1), (change.Trip.Id, change.Proposed.Id));
    }

    [Fact]
    public void Plan_DepartedTrip_BlocksItsVehicleUntilItEnds()
    {
        // V1 (base C) runs #10 until 12:30, so it cannot take #11, which leaves C at 12:00: V3 (100 km) is closer than V2 (200 km).
        // Before #10 left, V3 would have done it and V1 would be free at C.
        var v1 = NewVehicle(1, PointC);
        var v2 = NewVehicle(2, PointE);
        var v3 = NewVehicle(3, PointA);
        Trip[] trips = [NewTrip(10, PointA, PointB, 8, 4.5, D1, v1), NewTrip(11, PointC, PointA, 12, 1, D2, v2)];

        var change = Assert.Single(Plan(trips, [v1, v2, v3], now: Day.AddHours(9)).Changes);

        Assert.Equal((11, 3), (change.Trip.Id, change.Proposed.Id));
    }

    [Fact]
    public void Plan_WholeWeekDeparted_NoChangesAndSaysWhy()
    {
        var (trips, fleet) = Chainable();

        var plan = Plan(trips, fleet, now: Day.AddDays(8));

        Assert.Empty(plan.Changes);
        Assert.Equal((2, 2), (plan.TripsConsidered, plan.TripsDeparted));
        Assert.Equal(50, plan.BaselineDeadheadKm, 0);
        Assert.Equal(plan.BaselineDeadheadKm, plan.ProposedDeadheadKm);
        Assert.StartsWith("Los 2 servicios asignados de este periodo ya han salido", plan.Explanation.Summary.Es());
    }

    [Fact]
    public void Plan_OnlyTripDeparted_SaysItInSingular()
    {
        var v1 = NewVehicle(1, PointA);

        var plan = Plan([NewTrip(10, PointA, PointB, 8, 1, D1, v1)], [v1], now: Day.AddDays(1));

        Assert.StartsWith("El único servicio asignado de este periodo ya ha salido", plan.Explanation.Summary.Es());
    }

    [Fact]
    public void Plan_FutureWeek_NothingDeparted_SamePlanAsAlways()
    {
        var (trips, fleet) = Chainable();

        var plan = Plan(trips, fleet, now: Day.AddDays(-3));

        Assert.Single(plan.Changes);
        Assert.Equal(0, plan.TripsDeparted);
        Assert.DoesNotContain(plan.Explanation.Business, i => i.Title.Es() == "Servicios que ya han salido");
    }

    [Theory]
    [InlineData(0, 30, 0)]  // 02:30 CEST (first pass): later on the wall clock than 02:15, but it has not departed
    [InlineData(1, 14, 0)]  // 02:14 CET
    [InlineData(1, 15, 1)]  // 02:15 CET: leaves now
    public void Plan_AutumnClockChange_ComparesInstants(int hour, int minute, int departed)
    {
        // 25 Oct 2026: 02:00–03:00 happens twice in Madrid. #11 leaves at 02:15 CET = 01:15Z.
        var trip = TripAt(new DateTime(2026, 10, 25, 1, 15, 0), out var fleet);

        var plan = Plan([trip], fleet, now: new DateTime(2026, 10, 25, hour, minute, 0, DateTimeKind.Utc));

        Assert.Equal(departed, plan.TripsDeparted);
        Assert.Equal(1 - departed, plan.Changes.Count);
    }

    [Theory]
    [InlineData(0, 59, 0)]  // 01:59 CET
    [InlineData(1, 4, 0)]   // 03:04 CEST
    [InlineData(1, 5, 1)]   // 03:05 CEST: leaves now
    public void Plan_SpringClockChange_ComparesInstants(int hour, int minute, int departed)
    {
        // 29 Mar 2026: 02:00 jumps to 03:00 in Madrid. #11 leaves at 03:05 CEST = 01:05Z.
        var trip = TripAt(new DateTime(2026, 3, 29, 1, 5, 0), out var fleet);

        var plan = Plan([trip], fleet, now: new DateTime(2026, 3, 29, hour, minute, 0, DateTimeKind.Utc));

        Assert.Equal(departed, plan.TripsDeparted);
        Assert.Equal(1 - departed, plan.Changes.Count);
    }

    /// <summary>A trip from A with V2 (base C): V1 (base A) would save the approach. The start has no Kind, as read from the database.</summary>
    private static Trip TripAt(DateTime start, out Vehicle[] fleet)
    {
        var v2 = NewVehicle(2, PointC);
        fleet = [NewVehicle(1, PointA), v2];
        var trip = new Trip(PointA, PointB, new TimeWindow(start, start.AddHours(1)), 10, ServiceType.Regular);
        SetId(trip, 11);
        trip.AssignResources(D1, v2);
        return trip;
    }

    [Fact]
    public void Plan_LocalClock_IsRejected() =>
        Assert.ThrowsAny<ArgumentException>(() => Plan([], [], now: new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void Plan_Texts_UseThousandsSeparatorAndSingular()
    {
        // V2 is ≈1,112 km away from the only trip; V1 is on the spot.
        var far = NewLocation(9, 1112);
        var locations = new Dictionary<int, Location>(Locations) { [9] = far };
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, far);

        var plan = VehicleChainingPlanner.Plan([NewTrip(10, PointA, PointB, 8, 1, D1, v2)], [v1, v2], locations, false,
            new VehicleEligibilityService(), Day.AddDays(-7));

        Assert.StartsWith("1 cambio de vehículo: 1.11", plan.Explanation.Summary.Es());
    }

    [Fact]
    public void Plan_TinySaving_DoesNotSayZeroPercent()
    {
        // ≈3,000 km that nobody can save (locked) and 1 km that can be saved: 0.03 %.
        var near = NewLocation(8, 1);
        var far = NewLocation(9, 3000);
        var locations = new Dictionary<int, Location>(Locations) { [8] = near, [9] = far };
        var v1 = NewVehicle(1, far);
        var v2 = NewVehicle(2, near);
        var v3 = NewVehicle(3, PointA);
        Trip[] trips = [NewTrip(10, PointC, PointC, 8, 1, D1, v1, locked: true), NewTrip(11, PointA, PointB, 8, 1, D2, v2)];

        var plan = VehicleChainingPlanner.Plan(trips, [v1, v2, v3], locations, false, new VehicleEligibilityService(), Day.AddDays(-7));

        Assert.Single(plan.Changes);
        Assert.Contains("menos del 0,1 %", plan.Explanation.Summary.Es());
        Assert.DoesNotContain("(0 %", plan.Explanation.Summary.Es());
    }

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static TimeSpan Travel(Location from, Location to) => TravelAssumptions.TravelTime(from.Coordinate.DistanceTo(to.Coordinate));

    [Theory]
    [InlineData(0, true, true)]     // arrives exactly at departure
    [InlineData(1, true, false)]    // one tick late
    [InlineData(TimeSpan.TicksPerMinute, true, false)]
    [InlineData(-TimeSpan.TicksPerMinute, true, true)]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(TimeSpan.TicksPerMinute, false, false)]
    public void Plan_IdleVehicleAtBase_MustArriveLeavingNow(long ticksLate, bool returnToBase, bool arrives)
    {
        // #11 leaves B at 12:00 with V2 (250 km away). V1 (base A, 50 km) does nothing yet.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointE);
        var trip = NewTrip(11, PointB, PointA, 12, 1, D2, v2);
        var now = trip.ScheduledWindow.Start - Travel(PointA, PointB) + TimeSpan.FromTicks(ticksLate);

        var plan = Plan([trip], [v1, v2], returnToBase, now);

        Assert.Equal(arrives ? 1 : 0, plan.Changes.Count);
        Assert.Equal(0, plan.TripsDeparted);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Plan_FinishedService_LeavesItsDestinationNow_NotWhenItEnded(long ticksLate, bool arrives)
    {
        // V1 ended at A at 09:00 and has been free for hours, but it still has 50 km to go from now.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointE);
        Trip[] trips = [NewTrip(10, PointB, PointA, 8, 1, D1, v1), NewTrip(11, PointB, PointC, 12, 1, D2, v2)];
        var now = trips[1].ScheduledWindow.Start - Travel(PointA, PointB) + TimeSpan.FromTicks(ticksLate);

        Assert.Equal(arrives ? 1 : 0, Plan(trips, [v1, v2], now: now).Changes.Count);
    }

    [Theory]
    [InlineData(11.0, 1)]  // ends at 11:00: 60 min for 43
    [InlineData(11.5, 0)]  // ends at 11:30: 30 min for 43, although at 10:00 there were two hours left
    public void Plan_InProgress_IsFreeWhenItEnds_NotNow(double endHour, int changes)
    {
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointE);
        Trip[] trips = [NewTrip(10, PointB, PointA, 8, endHour - 8, D1, v1), NewTrip(11, PointB, PointC, 12, 1, D2, v2)];

        Assert.Equal(changes, Plan(trips, [v1, v2], now: Day.AddHours(10)).Changes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_FollowingDays_AreNotLimitedByNow(bool returnToBase)
    {
        // Same service on the next day: whatever the hour today, V1 gets there.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointE);
        var tomorrow = NewTrip(11, PointB, PointC, 12, 1, D2, v2, dayOffset: 1);

        Assert.Single(Plan([tomorrow], [v1, v2], returnToBase, Day.AddHours(22)).Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_FutureWeek_SameResultWhateverTheClockAndTheLastService(bool returnToBase)
    {
        var (trips, fleet) = Chainable();

        var near = Plan(trips, fleet, returnToBase, Day.AddDays(-1));
        var far = Plan(trips, fleet, returnToBase, Day.AddDays(-60), NewTrip(5, PointB, PointE, 1, 1, D1, fleet[0], dayOffset: -61));

        Assert.Equal((near.Changes.Count, near.BaselineDeadheadKm, near.ProposedDeadheadKm), (far.Changes.Count, far.BaselineDeadheadKm, far.ProposedDeadheadKm));
        Assert.Single(far.Changes);
    }

    [Fact]
    public void Plan_CurrentVehicleThatDoesNotArrive_IsKeptAndTheRestIsStillProposed()
    {
        // #9 leaves E in 5 minutes: nobody arrives, not even its own V3. It keeps V3, and #11 is still improved.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var v3 = NewVehicle(3, PointC);
        Trip[] trips = [NewTrip(9, PointE, PointE, 8, 1, D1, v3), NewTrip(11, PointA, PointB, 12, 1, D2, v2)];

        var plan = Plan(trips, [v1, v2, v3], now: Day.AddHours(8) - 5 * Minute);

        var change = Assert.Single(plan.Changes);
        Assert.Equal((11, 1), (change.Trip.Id, change.Proposed.Id));
        Assert.Contains("#9", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Conservan su autobús aunque no llegue").Detail.Es());
    }

    [Fact]
    public void Plan_CurrentVehicleThatDoesNotArrive_StillCompetesByKm()
    {
        // V2 (own, base 20 km away) is late: its last service ended far away. V1 is on time but farther (30 km): V2 stays.
        var v1 = NewVehicle(1, NewLocation(9, 20));
        var v2 = NewVehicle(2, NewLocation(8, 30));
        var locations = new Dictionary<int, Location>(Locations) { [8] = NewLocation(8, 30), [9] = NewLocation(9, 20) };
        var trip = NewTrip(11, PointB, PointA, 12, 1, D2, v2);

        var plan = VehicleChainingPlanner.Plan([trip], [v1, v2], locations, false, new VehicleEligibilityService(),
            Day.AddHours(12) - 27 * Minute, [NewTrip(5, PointA, PointE, 8, 1, D1, v2)]);

        Assert.Empty(plan.Changes);
        Assert.StartsWith("El cálculo no ha encontrado", plan.Explanation.Summary.Es());
    }

    [Fact]
    public void Plan_OwnTightServices_DoNotInvalidateTheReport()
    {
        // V1's own two services do not fit by the estimate (E → B in 6 min) and they are locked. A future week: not about now.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        var v3 = NewVehicle(3, PointA);
        Trip[] trips =
        [
            NewTrip(10, PointA, PointE, 8, 1, D1, v1, locked: true), NewTrip(11, PointB, PointA, 9.1, 1, D1, v1, locked: true),
            NewTrip(12, PointA, PointB, 15, 1, D2, v2)
        ];

        var plan = Plan(trips, [v1, v2, v3]);

        Assert.Equal(12, Assert.Single(plan.Changes).Trip.Id);
        Assert.Contains("#11", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Conservan su autobús aunque no llegue").Detail.Es());
    }

    [Fact]
    public void Plan_OwnTightServices_AreTheLastResort_WhenAnotherOneArrives()
    {
        // V1 ends #10 at B at 09:00 and does not reach its #11 (A, 09:06). V3 is at A: as before, it takes it.
        var v1 = NewVehicle(1, PointA);
        var v3 = NewVehicle(3, PointA);
        Trip[] trips = [NewTrip(10, PointA, PointB, 8, 1, D1, v1, locked: true), NewTrip(11, PointA, PointA, 9.1, 1, D1, v1)];

        var plan = Plan(trips, [v1, v3]);

        Assert.Equal((11, 3), (Assert.Single(plan.Changes).Trip.Id, plan.Changes[0].Proposed.Id));
    }

    [Fact]
    public void Plan_VehicleTakenByAnEarlierService_IsGivenBackWhenNobodyElseReachesItsOwn()
    {
        // At 08:00, V2 (at B) is the closest to #10 (leaves B at 08:50), but then nobody reaches #11 (leaves A at 09:00, V2's own):
        // V1 is 100 km away. #10 goes back to V1 (50 km, arrives), #11 stays with V2, and #12 is still improved.
        var v1 = NewVehicle(1, PointC);
        var v2 = NewVehicle(2, PointB);
        var v3 = NewVehicle(3, PointE);
        Trip[] trips = [NewTrip(10, PointB, PointC, 8 + 50 / 60d, 1, D1, v1), NewTrip(11, PointA, PointA, 9, 1, D2, v2), NewTrip(12, PointA, PointA, 20, 1, D2, v3)];

        var plan = Plan(trips, [v1, v2, v3], now: Day.AddHours(8));

        Assert.Equal((12, 2), (Assert.Single(plan.Changes).Trip.Id, plan.Changes[0].Proposed.Id));
        Assert.DoesNotContain(plan.Explanation.Business, i => i.Title.Es() == "Conservan su autobús aunque no llegue");
    }

    /// <summary>
    /// Random plans without overlaps and any clock: the estimate never invalidates the report, and whatever is proposed has no vehicle in
    /// two services at once and reaches every new service leaving at <c>max(now, end of the previous one)</c> (checked here on its own).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_RandomPlans_AreNeverInvalidatedAndNeverImpossible(bool returnToBase)
    {
        Location[] places = [PointA, PointB, PointC, PointE];
        var withChanges = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var fleet = Enumerable.Range(1, 4).Select(i => NewVehicle(i, places[random.Next(4)])).ToArray();
            var trips = new List<Trip>();
            foreach (var vehicle in fleet)
            {
                var hour = random.NextDouble() * 4;
                while (hour < 20)
                {
                    var hours = 0.5 + random.NextDouble() * 2;
                    trips.Add(NewTrip(trips.Count + 1, places[random.Next(4)], places[random.Next(4)], hour, hours, D1, vehicle, locked: random.Next(6) == 0));
                    hour += hours + random.NextDouble() * 4; // often too tight for the estimate
                }
            }

            var now = Day.AddHours(random.NextDouble() * 20);
            var lasts = fleet.Where(_ => random.Next(2) == 0)
                .Select((v, i) => NewTrip(1000 + i, places[random.Next(4)], places[random.Next(4)], -3, 2, D1, v)).ToArray();

            var plan = Plan(trips, fleet, returnToBase, now, lasts);

            Assert.DoesNotContain("no tiene ningún vehículo apto", plan.Explanation.Summary.Es());
            withChanges += plan.Changes.Count > 0 ? 1 : 0;
            var changed = plan.Changes.ToDictionary(c => c.Trip.Id, c => c.Proposed.Id);
            foreach (var vehicle in fleet)
            {
                var own = trips.Where(t => changed.GetValueOrDefault(t.Id, t.AssignedVehicleId!.Value) == vehicle.Id).OrderBy(t => t.ScheduledWindow.Start).ToList();
                var previous = lasts.SingleOrDefault(t => t.AssignedVehicleId == vehicle.Id && t.HasDeparted(now));
                foreach (var service in own)
                {
                    Assert.True(previous is null || previous.ScheduledWindow.End <= service.ScheduledWindow.Start, $"seed {seed}: #{service.Id} overlaps");
                    if (changed.ContainsKey(service.Id))
                    {
                        var home = Locations[vehicle.HomeLocationId!.Value];
                        var back = returnToBase && previous is not null
                            && previous.ScheduledWindow.Start.AddHours(1).Date != service.ScheduledWindow.Start.AddHours(1).Date; // Madrid, winter
                        var position = previous is null || back ? home : Locations[previous.DestinationLocationId];
                        var free = previous is null ? now : previous.ScheduledWindow.End + (back ? Travel(Locations[previous.DestinationLocationId], home) : TimeSpan.Zero);
                        var leaves = free > now ? free : now;
                        Assert.True(leaves + Travel(position, Locations[service.OriginLocationId]) <= service.ScheduledWindow.Start, $"seed {seed}: #{service.Id} not reached");
                    }

                    previous = service;
                }
            }
        }

        Assert.True(withChanges > 50, $"only {withChanges} plans with changes");
    }

    [Fact]
    public void Plan_UnfitVehicle_AndTheOthersOnlyLateByTheEstimate_DoesNotInvalidateTheReport()
    {
        // V2 is not fit for #11 any more and the others would only get there if they had left earlier: #11 stays as it is, the summary says
        // so, and #12 is improved.
        var v1 = NewVehicle(1, PointE);
        var v2 = NewVehicle(2, PointB, seats: 5);
        var v3 = NewVehicle(3, PointC);
        Trip[] trips = [NewTrip(11, PointB, PointA, 12, 1, D2, v2), NewTrip(12, PointE, PointE, 20, 1, D1, v3)];

        var plan = Plan(trips, [v1, v2, v3], now: Day.AddHours(11.9));

        var change = Assert.Single(plan.Changes);
        Assert.Equal((12, 1), (change.Trip.Id, change.Proposed.Id));
        Assert.Contains("#11", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Conservan un autobús que no es apto").Detail.Es());
        Assert.DoesNotContain(plan.Explanation.Business, i => i.Title.Es() == "Conservan su autobús aunque no llegue");
    }

    [Fact]
    public void Plan_UnfitVehicleTakenByAnEarlierService_IsGivenBackToo()
    {
        // V2 (at B) is the closest to #10 but has just been sent to the workshop for #11's hours, and V1 does not reach #11 from now.
        // #11 keeps V2 (nobody better) and so V2 does not get #10.
        var v1 = NewVehicle(1, PointC);
        var v2 = NewVehicle(2, PointB);
        var v3 = NewVehicle(3, PointE);
        Trip[] trips = [NewTrip(10, PointB, PointC, 8 + 50 / 60d, 0.5, D1, v1), NewTrip(11, PointA, PointA, 9.5, 1, D2, v2), NewTrip(12, PointA, PointA, 20, 1, D2, v3)];
        v2.AddUnavailability(new VehicleUnavailability(v2.Id, new TimeWindow(Day.AddHours(9.5), Day.AddHours(11)), VehicleUnavailabilityType.Repair));

        var plan = Plan(trips, [v1, v2, v3], now: Day.AddHours(8.1));

        Assert.DoesNotContain("no tiene ningún vehículo apto", plan.Explanation.Summary.Es());
        Assert.DoesNotContain(plan.Changes, c => c.Trip.Id is 10 or 11);
        Assert.Contains(plan.Changes, c => c.Trip.Id == 12);
        Assert.Contains("#11", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Conservan un autobús que no es apto").Detail.Es());
    }

    [Fact]
    public void Plan_NobodyIsFit_KeepsTheWholePlan()
    {
        // Nobody has the seats, whatever the estimate says (SPEC 0043 R6).
        var v1 = NewVehicle(1, PointB, seats: 5);
        var v2 = NewVehicle(2, PointB, seats: 5);

        var plan = Plan([NewTrip(11, PointB, PointA, 12, 1, D2, v2)], [v1, v2], now: Day.AddHours(11.5));

        Assert.Empty(plan.Changes);
        Assert.Contains("#11 no tiene ningún vehículo apto", plan.Explanation.Summary.Es());
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    public void Plan_LastServiceOfToday_OutsideThePlan_GivesThePosition(long ticksLate, bool returnToBase, bool arrives)
    {
        // V1's base is next to #11, but its last service (of another base, or before the period) ended at A, 50 km away.
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);
        var trip = NewTrip(11, PointB, PointB, 12, 1, D2, v2);
        var now = trip.ScheduledWindow.Start - Travel(PointA, PointB) + TimeSpan.FromTicks(ticksLate);

        Assert.Equal(arrives ? 1 : 0, Plan([trip], [v1, v2], returnToBase, now, NewTrip(5, PointB, PointA, 8, 1, D1, v1)).Changes.Count);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Plan_ReturnToBase_LastServiceOfAnotherDay_IsAtBaseAfterTheWayBack(long ticksLate, bool arrives)
    {
        // V1 (base B) ends at A at 23:30 Madrid. With return to base it goes A → B → C; #11 leaves C after midnight.
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointE);
        var last = NewTrip(5, PointB, PointA, 21.5, 1, D1, v1);
        var start = last.ScheduledWindow.End + Travel(PointA, PointB) + Travel(PointB, PointC) - TimeSpan.FromTicks(ticksLate);
        var trip = new Trip(PointC, PointC, new TimeWindow(start, start.AddHours(1)), 10, ServiceType.Regular);
        SetId(trip, 11);
        trip.AssignResources(D2, v2);

        Assert.Equal(arrives ? 1 : 0, Plan([trip], [v1, v2], true, last.ScheduledWindow.End, last).Changes.Count);
    }

    [Theory]
    [InlineData(false, 0)] // it stayed at A, 50 km away: 20 minutes are not enough
    [InlineData(true, 1)]  // yesterday's: back at its base (B)
    public void Plan_LastServiceOfAnotherDay_ByMode(bool returnToBase, int changes)
    {
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);
        var trip = NewTrip(11, PointB, PointB, 12, 1, D2, v2);

        Assert.Equal(changes, Plan([trip], [v1, v2], returnToBase, Day.AddHours(12) - 20 * Minute, NewTrip(5, PointB, PointA, 8, 1, D1, v1, dayOffset: -1)).Changes.Count);
    }

    [Fact]
    public void Plan_LastServiceNotDepartedOrCancelled_IsIgnored()
    {
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);
        var later = NewTrip(5, PointE, PointE, 11.75, 1, D1, v1);
        var cancelled = NewTrip(6, PointE, PointE, 8, 4, D1, v1);
        cancelled.Cancel();

        Assert.Single(Plan([NewTrip(11, PointB, PointC, 12, 1, D2, v2)], [v1, v2], false, Day.AddHours(11.5), later, cancelled).Changes);
    }

    [Fact]
    public void Plan_LastServiceInAnUnknownLocation_FallsBackToTheBase()
    {
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);

        Assert.Single(Plan([NewTrip(11, PointB, PointC, 12, 1, D2, v2)], [v1, v2], false, Day.AddHours(11.5), NewTrip(5, PointB, NewLocation(99, 10), 8, 1, D1, v1)).Changes);
    }

    [Fact]
    public void Plan_DepartedServiceOfThePlan_OlderThanTheLastOneOutsideIt_DoesNotHideIt()
    {
        // V1 did #10 in the plan (ends at B) and then another one outside it, which ended at E: it is not at B any more.
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);
        Trip[] trips = [NewTrip(10, PointA, PointB, 6, 1, D1, v1), NewTrip(11, PointB, PointC, 12, 1, D2, v2)];

        Assert.Empty(Plan(trips, [v1, v2], false, Day.AddHours(11.5), NewTrip(5, PointB, PointE, 8, 3, D1, v1)).Changes);
    }

    [Fact]
    public void Plan_Explanation_SaysFromWhereAndWhenItWouldLeave()
    {
        var v1 = NewVehicle(1, PointB);
        var v2 = NewVehicle(2, PointC);

        var plan = Plan([NewTrip(11, PointB, PointC, 12, 1, D2, v2)], [v1, v2], false, Day.AddHours(11), NewTrip(5, PointB, PointA, 8, 1, D1, v1));

        var arrival = Assert.Single(Assert.Single(plan.Changes).Explanation.Business, i => i.Title.Es() == "Llega a tiempo").Detail;
        Assert.Contains("12:00 (ahora)", arrival.Es()); // 11:00Z
        Assert.Contains("servicio #5", arrival.Es());
        Assert.Contains("≈43 min", arrival.Es());
        Assert.Contains("margen: 17 min", arrival.Es());
        Assert.Contains("antes de ahora", Assert.Single(plan.Explanation.Business, i => i.Title.Es() == "Llegada").Detail.Es());
    }

    /// <summary>A 20-minute trip at a Madrid local time, given its offset in hours.</summary>
    private static Trip MadridTrip(int id, Location from, Location to, string local, int offset, Vehicle vehicle)
    {
        var start = DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture).AddHours(-offset);
        var trip = new Trip(from, to, new TimeWindow(start, start.AddMinutes(20)), 10, ServiceType.Regular);
        SetId(trip, id);
        trip.AssignResources(D1, vehicle);
        return trip;
    }

    [Theory]
    [InlineData("2026-11-23T00:30", 1, "2026-11-23T23:30", 1, true)]   // Monday, winter: 23:30Z of Sunday and 22:30Z of Monday
    [InlineData("2026-11-29T00:30", 1, "2026-11-29T23:30", 1, true)]   // Sunday
    [InlineData("2026-11-22T23:30", 1, "2026-11-23T00:30", 1, false)]  // Sunday → Monday: same UTC day
    [InlineData("2026-07-06T00:30", 2, "2026-07-06T23:30", 2, true)]   // summer
    [InlineData("2026-07-05T23:30", 2, "2026-07-06T00:30", 2, false)]
    [InlineData("2026-03-29T00:30", 1, "2026-03-29T23:30", 2, true)]   // 23-hour Sunday
    [InlineData("2026-03-29T23:30", 2, "2026-03-30T00:30", 2, false)]
    [InlineData("2026-10-25T00:30", 2, "2026-10-25T23:30", 1, true)]   // 25-hour Sunday
    [InlineData("2026-10-25T23:30", 1, "2026-10-26T00:30", 1, false)]
    public void Plan_ReturnToBase_SplitsByMadridDay(string first, int firstOffset, string second, int secondOffset, bool sameDay)
    {
        // V1 (base A): A→B and B→A. The same day, no empty km; on another one, back to the base and out again (100 km).
        var v1 = NewVehicle(1, PointA);
        Trip[] trips = [MadridTrip(10, PointA, PointB, first, firstOffset, v1), MadridTrip(11, PointB, PointA, second, secondOffset, v1)];

        var plan = Plan(trips, [v1], returnToBase: true, now: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(sameDay ? 0 : 100, plan.BaselineDeadheadKm, 0);
    }

    [Fact]
    public void Plan_ReturnToBase_AfterMadridMidnight_TheVehicleLeavesFromItsBase()
    {
        // V1 (base A) ends at B on Sunday at 23:50 Madrid; on Monday at 00:45 (same UTC day) it is back at A, 0 km away from #11.
        var v1 = NewVehicle(1, PointA);
        var v2 = NewVehicle(2, PointC);
        Trip[] trips = [MadridTrip(10, PointA, PointB, "2026-11-22T23:30", 1, v1), MadridTrip(11, PointA, PointA, "2026-11-23T00:45", 1, v2)];

        var change = Assert.Single(Plan(trips, [v1, v2], returnToBase: true, now: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)).Changes);

        Assert.Equal((11, 1), (change.Trip.Id, change.Proposed.Id));
        Assert.Contains("Su base", Assert.Single(change.Explanation.Business, i => i.Title.Es() == "Origen del vehículo").Detail.Es());
    }
}
