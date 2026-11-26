using System.Reflection;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Recommendation;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

public class RecommendationServiceTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 6, 0, 0, DateTimeKind.Utc);
    private readonly World _w = new();

    private RecommendationService Service => new(_w, new VehicleEligibilityService(), new FixedClock(Now));

    private Task<RecommendationDto> Propose(int? baseId = null) => Service.ProposeAsync(new RecommendationRequest(null, baseId, Now.AddDays(-1), Now.AddDays(60)));

    [Fact]
    public async Task Propose_EachChoiceCountsForTheNext()
    {
        _w.Driver(1, 0); _w.Driver(2, 3);
        _w.Vehicle(11, 0); _w.Vehicle(12, 3);
        _w.Trip(101, 2, 4, booking: 1);
        _w.Trip(102, 3, 5, booking: 2); // overlaps

        var lines = (await Propose()).Lines;

        Assert.Equal([(101, 1, 11), (102, 2, 12)], lines.Select(l => (l.TripId, l.Driver!.Id, l.Vehicle!.Id)));
        Assert.All(lines, l => Assert.Equal(RecommendationLineStatus.Proposed, l.Status));
    }

    [Fact]
    public async Task Propose_NobodyLeft_IsNoSolution()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0);
        _w.Trip(101, 2, 4, booking: 1); _w.Trip(102, 3, 5, booking: 2);

        var line = (await Propose()).Lines[1];

        Assert.Equal(RecommendationLineStatus.NoSolution, line.Status);
        Assert.Null(line.Driver);
        Assert.Contains("ningún conductor legal ni vehículo apto", line.Reason.Es());
    }

    [Fact]
    public async Task Propose_AccumulatedDriving_IsNeverProposedPastTheLimit()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0);
        // 4 h + 4 h + 4 h in one day: the third would be 12 h.
        _w.Trip(101, 1, 5, booking: 1); _w.Trip(102, 6, 10, booking: 2); _w.Trip(103, 11, 15, booking: 3);

        var lines = (await Propose()).Lines;

        Assert.Equal([RecommendationLineStatus.Proposed, RecommendationLineStatus.Proposed, RecommendationLineStatus.NoSolution], lines.Select(l => l.Status));
        Assert.Equal(0, lines[2].Drivers!.Valid);
    }

    [Fact]
    public async Task Propose_LaterServiceAlreadyAssigned_IsNotMadeIllegal()
    {
        var driver = _w.Driver(1, 0); var vehicle = _w.Vehicle(11, 0);
        _w.Trip(200, 6, 10, booking: 9).AssignResources(driver, vehicle);
        _w.Trip(201, 11, 15, booking: 9).AssignResources(driver, vehicle); // 8 h already
        _w.Trip(101, 1, 5, booking: 1);

        Assert.Equal(RecommendationLineStatus.NoSolution, Assert.Single((await Propose()).Lines).Status);
    }

    [Fact]
    public async Task Propose_BeyondThirtyKm_SaysHowManyAreFarther()
    {
        _w.Driver(1, 31); _w.Driver(2, 50); _w.Driver(3, null); _w.Vehicle(11, 0); _w.Vehicle(12, 40);
        _w.Trip(101, 2, 4, booking: 1);

        var line = Assert.Single((await Propose()).Lines);

        Assert.Equal(RecommendationLineStatus.NoSolution, line.Status);
        Assert.Equal(new ReachCounts(3, 2, 0, 1), line.Drivers);
        Assert.Equal(new ReachCounts(2, 1, 0, 0), line.Vehicles);
        Assert.Contains("Sin solución a 30 km. Más lejos: 2 conductores legales y 1 vehículo apto", line.Reason.Es());
        Assert.Contains("Sin posición conocida: 1 conductor y 0 vehículos", line.Reason.Es());
    }

    [Fact]
    public async Task Propose_SameBooking_KeepsDriverAndVehicleOnAnotherDayBeyondTheSteps()
    {
        // Day 2 starts 70 km away from the reference location; driver 2 and vehicle 12 live there.
        _w.Driver(1, 0); _w.Driver(2, 70); _w.Vehicle(11, 0); _w.Vehicle(12, 70);
        _w.Trip(101, 2, 4, booking: 1, fromKm: 0, toKm: 70);
        _w.Trip(102, 26, 28, booking: 1, fromKm: 70, toKm: 0);

        var lines = (await Propose()).Lines;

        Assert.Equal([(1, 11), (1, 11)], lines.Select(l => (l.Driver!.Id, l.Vehicle!.Id)));
        Assert.True(lines[1].Driver!.SameBooking && lines[1].Vehicle!.SameBooking);
        Assert.Equal(70, lines[1].Driver!.DistanceKm!.Value, 0);
        Assert.Null(lines[1].Driver!.StepKm);
    }

    [Fact]
    public async Task Propose_WhoDoesNotReachTheNextService_IsDiscarded()
    {
        var near = _w.Driver(1, 0); _w.Driver(2, 20); var bus = _w.Vehicle(11, 0); _w.Vehicle(12, 20);
        // Held at 4:30, 200 km from where the trip ends at 4:00.
        _w.Trip(200, 4.5, 6, booking: 9, fromKm: 200, toKm: 200).AssignResources(near, bus);
        _w.Trip(101, 2, 4, booking: 1);

        var line = Assert.Single((await Propose()).Lines);

        Assert.Equal((2, 12), (line.Driver!.Id, line.Vehicle!.Id));
    }

    [Fact]
    public async Task Propose_AssignedLockedCancelledDepartedOrHalfAssigned_AreNotTouched()
    {
        var driver = _w.Driver(1, 0); var vehicle = _w.Vehicle(11, 0);
        _w.Trip(1, 2, 3, booking: 1).AssignResources(driver, vehicle);
        _w.Trip(2, 30, 31, booking: 2).Lock();
        _w.Trip(3, 40, 41, booking: 3).Cancel();
        _w.Trip(4, -1, 1, booking: 4);
        _w.Trip(5, 0, 1, booking: 5); // departs exactly now
        World.Set(_w.Trip(6, 50, 51, booking: 6), nameof(Trip.AssignedDriverId), 1);
        var before = _w.State();

        Assert.Empty((await Propose()).Lines);
        Assert.Equal(before, _w.State());
    }

    [Fact]
    public async Task Propose_NeverChangesWhatItReads()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0); _w.Trip(101, 2, 4, booking: 1);
        await Propose();
        Assert.Null(_w.Trips.Single().AssignedDriverId);
    }

    [Fact]
    public async Task Propose_BaseFiltersTripsNotCandidates()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0); // base 1
        _w.Trip(101, 2, 4, booking: 1);
        _w.Trip(102, 6, 8, booking: 2).MoveToBase(2);

        var line = Assert.Single((await Propose(baseId: 2)).Lines);

        Assert.Equal((102, 1), (line.TripId, line.Driver!.Id));
    }

    [Fact]
    public void Select_HundredNearestTrips_AndThirtyOneDaysInclusive()
    {
        var many = Enumerable.Range(1, 130).Select(i => _w.Trip(i, i, i + 0.5, booking: i)).Reverse().ToList();
        var (trips, leftOut) = RecommendationService.Select(many);
        Assert.Equal((100, 30), (trips.Count, leftOut));
        Assert.Equal(Enumerable.Range(1, 100), trips.Select(t => t.Id));

        var edge = new[] { _w.Trip(1001, 1, 2, 1), _w.Trip(1002, 1 + 31 * 24, 3 + 31 * 24, 2), _w.Trip(1003, 1 + 31 * 24 + 1 / 60.0, 3 + 31 * 24, 3) };
        (trips, leftOut) = RecommendationService.Select(edge);
        Assert.Equal([1001, 1002], trips.Select(t => t.Id));
        Assert.Equal(1, leftOut);
        Assert.Equal((0, 0), (RecommendationService.Select([]).Trips.Count, RecommendationService.Select([]).LeftOut));
    }

    [Fact]
    public void Select_BookingsByFirstPendingDeparture_ThenInTimeOrder()
    {
        var trips = new[] { _w.Trip(1, 5, 6, 2), _w.Trip(2, 1, 2, 7), _w.Trip(3, 9, 10, 7), _w.Trip(4, 3, 4, 2) };
        Assert.Equal([2, 3, 4, 1], RecommendationService.Select(trips).Trips.Select(t => t.Id));
    }

    [Fact]
    public async Task Check_ChangedLine_RevalidatesEveryLineAndTheInvalidOneDoesNotCount()
    {
        _w.Driver(1, 0); _w.Driver(2, 1); _w.Vehicle(11, 0); _w.Vehicle(12, 1);
        _w.Trip(101, 2, 4, 1); _w.Trip(102, 3, 5, 2); _w.Trip(103, 4.5, 6, 3);

        var lines = await Service.CheckAsync(new RecommendationCheckRequest(
            [new(101, 2, 11), new(102, 2, 12), new(103, 2, 12)]));

        Assert.Equal([RecommendationLineStatus.Proposed, RecommendationLineStatus.Invalid, RecommendationLineStatus.Proposed], lines.Select(l => l.Status));
        Assert.NotEmpty(lines[1].Reason.Es());
        Assert.NotNull(lines[1].Explanation);
    }

    [Fact]
    public async Task Check_AProposedPairThatNoLongerArrives_IsInvalid_ButAPersonCanChooseIt()
    {
        // By hand, driver 1 and bus 11 take 101, which leaves them 100 km away when 102 departs half an hour later.
        _w.Driver(1, 0); _w.Driver(2, 0); _w.Vehicle(11, 0); _w.Vehicle(12, 0);
        _w.Trip(101, 2, 3, 1, fromKm: 0, toKm: 100); _w.Trip(102, 3.5, 4.5, 2); _w.Trip(103, 3.5, 4.5, 3);

        var lines = await Service.CheckAsync(new([new(101, 1, 11, Manual: true), new(102, 1, 11), new(103, 1, 11, Manual: true)]));

        Assert.Equal([RecommendationLineStatus.Proposed, RecommendationLineStatus.Invalid, RecommendationLineStatus.Proposed], lines.Select(l => l.Status));
        Assert.Equal("El conductor no llega desde su servicio #101 a 70 km/h (estimación)", lines[1].Reason.Es());
        Assert.Contains("no llega desde su servicio #101", lines[2].Driver!.Late.Es());

        var vehicleOnly = await Service.CheckAsync(new([new(101, 1, 11, Manual: true), new(102, 2, 11)]));
        Assert.Equal("El vehículo no llega desde su servicio #101 a 70 km/h (estimación)", vehicleOnly[1].Reason.Es());
    }

    /// <summary>Nothing has been saved: a proposal must not say that the assignment is done.</summary>
    [Fact]
    public async Task Propose_AndCheck_ExplainAProposalNotAnAssignment()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0); _w.Trip(101, 2, 4, 1); _w.Trip(102, 3, 5, 2);

        var proposed = (await Propose()).Lines[0];
        var rejected = (await Service.CheckAsync(new([new(101, 1, 11), new(102, 1, 11, Manual: true)])))[1];

        Assert.StartsWith("Propuesta sin guardar", proposed.Reason.Es());
        Assert.StartsWith("No cumple, no se aplicaría", rejected.Explanation!.Summary.Es());
        Assert.All([proposed, rejected], l =>
        {
            Assert.DoesNotContain(l.Explanation!.Flow, s => s.Component is "Trip.AssignResources" or nameof(DriverAllocationService));
            Assert.Equal(nameof(AllocationEvaluator), l.Explanation.Flow[0].Component);
        });
        Assert.Equal(DispatchGuard.Core.ValueObjects.ExplanationOutcome.Failed, rejected.Explanation.Flow[0].Outcome);
    }

    [Fact]
    public async Task Check_TripChangedMeanwhile_SaysWhy()
    {
        var driver = _w.Driver(1, 0); var vehicle = _w.Vehicle(11, 0);
        _w.Trip(101, 2, 4, 1).AssignResources(driver, vehicle);
        _w.Trip(102, 30, 31, 2).Cancel();
        _w.Trip(103, 40, 41, 3).Lock();

        var lines = await Service.CheckAsync(new RecommendationCheckRequest([new(101, 1, 11), new(102, 1, 11), new(103, 1, 11)]));

        Assert.All(lines, l => Assert.Equal(RecommendationLineStatus.Invalid, l.Status));
        Assert.Equal(["El servicio #101 ya está asignado", "El servicio #102 está cancelado", "El servicio #103 está bloqueado"], lines.Select(l => l.Reason).Es());
    }

    [Fact]
    public async Task Check_BadInput_IsRejected()
    {
        _w.Driver(1, 0); _w.Vehicle(11, 0); _w.Trip(101, 2, 4, 1);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.CheckAsync(new([new(101, 1, 11), new(101, 1, 11)])));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.CheckAsync(new(Enumerable.Range(1, 101).Select(i => new RecommendationLineInput(i, 1, 1)).ToList())));
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() => Service.CheckAsync(new([new(999, 1, 11)])));
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() => Service.CheckAsync(new([new(101, 99, 11)])));
        Assert.Empty(await Service.CheckAsync(new(null)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.ProposeAsync(new RecommendationRequest()));
    }

    [Fact]
    public async Task Options_TakeTheOtherLinesIntoAccount()
    {
        _w.Driver(1, 0); _w.Driver(2, 40); _w.Vehicle(11, 0); _w.Vehicle(12, 1);
        _w.Trip(101, 2, 4, 1); _w.Trip(102, 3, 5, 2);

        var options = await Service.OptionsAsync(new RecommendationOptionsRequest(102, [new(101, 1, 11), new(102, 2, 12)]));

        Assert.Equal([2], options.Drivers.Select(d => d.Id)); // legal, beyond the steps: the person may choose it
        Assert.Null(options.Drivers[0].StepKm);
        Assert.Equal([12], options.Vehicles.Select(v => v.Id));
    }

    [Fact]
    public async Task InsideTheStep_ThePanelOrderDecides_InTheProposalAndInTheBadge()
    {
        // Driver 1 is based at the departure (first in the panel) but now 12 km away; driver 2 is 10 km away: same step.
        var first = _w.Driver(1, 0); _w.Driver(2, 10); _w.Vehicle(11, 0); var other = _w.Vehicle(12, 300);
        _w.Trip(200, -2, 0, booking: 9, fromKm: 0, toKm: 12).AssignResources(first, other);
        _w.Trip(101, 2, 4, booking: 1);

        var line = Assert.Single((await Propose()).Lines);

        Assert.Equal((1, 12.0), (line.Driver!.Id, line.Driver.DistanceKm!.Value));
        Assert.Equal(1, (await Service.ForTripAsync(101)).DriverId);
    }

    [Fact]
    public async Task ForTrip_IsWhatTheProposalWouldPick_OrNothing()
    {
        _w.Driver(1, 20); _w.Driver(2, 16); _w.Vehicle(11, 0); _w.Trip(101, 2, 4, 1);
        var badge = await Service.ForTripAsync(101);
        var line = Assert.Single((await Propose()).Lines);
        Assert.Equal((line.Driver!.Id, line.Vehicle!.Id), (badge.DriverId!.Value, badge.VehicleId!.Value));
        Assert.Equal(2, badge.DriverId);
        Assert.Equal(Now, badge.CalculatedAt);

        _w.Trip(102, 2, 4, 2, fromKm: 300, toKm: 300);
        Assert.Null((await Service.ForTripAsync(102)).DriverId);
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(() => Service.ForTripAsync(999));
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    /// <summary>The database: each read hands out new copies, as an untracked query does.</summary>
    private sealed class World : IPlanningSnapshotSource
    {
        private readonly Dictionary<double, Location> _locations = [];
        private readonly List<Driver> _drivers = [];
        private readonly List<Vehicle> _vehicles = [];
        public List<Trip> Trips { get; } = [];

        public Location At(double km)
        {
            if (_locations.TryGetValue(km, out var known)) return known;
            var location = new Location($"km {km}", "Calle 1", new GeoCoordinate(40m + (decimal)(km / 111.19492664), -3.7m), "Madrid", "Madrid");
            Set(location, nameof(Entity.Id), _locations.Count + 1);
            return _locations[km] = location;
        }

        public Driver Driver(int id, double? homeKm)
        {
            var driver = new Driver($"1234567{id}A", "Nombre", $"Apellido{id}");
            Set(driver, nameof(Entity.Id), id);
            if (homeKm is double km) driver.SetHomeLocation(At(km));
            _drivers.Add(driver);
            return driver;
        }

        public Vehicle Vehicle(int id, double? homeKm)
        {
            var vehicle = new Vehicle($"PLATE{id}", "Marca", "Modelo", 50, false, true);
            Set(vehicle, nameof(Entity.Id), id);
            if (homeKm is double km) vehicle.SetHomeLocation(At(km));
            _vehicles.Add(vehicle);
            return vehicle;
        }

        /// <summary>Hours from <see cref="Now"/>.</summary>
        public Trip Trip(int id, double startHours, double endHours, int booking, double fromKm = 0, double toKm = 0)
        {
            var trip = new Trip(At(fromKm), At(toKm), new TimeWindow(Now.AddHours(startHours), Now.AddHours(endHours)), 20, ServiceType.Regular);
            Set(trip, nameof(Entity.Id), id);
            Set(trip, nameof(DispatchGuard.Core.Entities.Trip.BookingId), booking);
            Trips.Add(trip);
            return trip;
        }

        public string State() => string.Join(";", Trips.Select(t => $"{t.Id}:{t.Status}:{t.AssignedDriverId}:{t.AssignedVehicleId}:{t.IsLocked}:{t.IsActive}"));

        public static void Set(Entity entity, string property, object value) =>
            entity.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.DeclaringType!.GetProperty(property)!.SetValue(entity, value);

        private static Trip Copy(Trip trip) =>
            (Trip)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(trip, null)!;

        public Task<IReadOnlyList<Trip>> PendingAsync(DateTime now, int? bookingId, int? baseId, DateTime? from, DateTime? to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trip>>(Trips.Where(t => t.ScheduledWindow.Start > now && t is { IsActive: true, IsLocked: false, AssignedDriverId: null, AssignedVehicleId: null }
                && (bookingId is null || t.BookingId == bookingId) && (baseId is null || t.BaseId == baseId)).Select(Copy).ToList());

        public Task<IReadOnlyList<Trip>> TripsAsync(IReadOnlyCollection<int> tripIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trip>>(Trips.Where(t => tripIds.Contains(t.Id)).Select(Copy).ToList());

        public Task<PlanningSnapshot> LoadAsync(DateTime from, DateTime to, IReadOnlyCollection<int> bookingIds, CancellationToken ct = default) =>
            Task.FromResult(new PlanningSnapshot(Trips.Select(Copy).ToList(), _drivers, _vehicles, _locations.Values.ToList(), new Dictionary<int, string>()));

        public Task<bool> BookingExistsAsync(int bookingId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> BaseExistsAsync(int baseId, CancellationToken ct = default) => Task.FromResult(true);
    }
}
