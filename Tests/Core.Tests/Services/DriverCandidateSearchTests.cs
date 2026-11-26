using System.Reflection;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

public class DriverCandidateSearchTests
{
    private static readonly DateTime TripStart = new(2026, 12, 1, 8, 0, 0);

    // Origin in Madrid (Puerta del Sol). ~0.009° latitude ≈ 1 km.
    private static readonly Location Origin = NewLocation(1, 40.4168m, -3.7038m);
    private static readonly Location Destination = NewLocation(2, 40.9000m, -3.7038m);

    private static Location NewLocation(int id, decimal lat, decimal lon)
    {
        var location = new Location($"L{id}", "Main St 1", new GeoCoordinate(lat, lon), "Madrid", "Madrid");
        SetId(location, id);
        return location;
    }

    /// <summary>A location <paramref name="km"/> km north of the origin.</summary>
    private static Location KmNorth(int id, double km) => NewLocation(id, Origin.Coordinate.Latitude + (decimal)(km / 111.195), -3.7038m);

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);

    private static Driver NewDriver(int id, Location? home, bool allowsAuto = true, bool external = false)
    {
        var driver = new Driver($"ID{id}", $"Name{id}", "Surname", external, allowsAuto);
        SetId(driver, id);
        if (home != null)
        {
            driver.SetHomeLocation(home);
        }

        return driver;
    }

    private static Trip NewTrip(Location? destination = null, int id = 100)
    {
        var trip = new Trip(Origin, destination ?? Destination, new TimeWindow(TripStart, TripStart.AddHours(3)), 10, ServiceType.Regular);
        SetId(trip, id);
        return trip;
    }

    private static DriverCandidateSearch NewSearch(
        IEnumerable<Driver> drivers,
        FakeLegalValidationService? legal = null,
        Dictionary<int, List<Trip>>? tripsByDriver = null,
        IEnumerable<Trip>? bookingTrips = null,
        Dictionary<int, double>? hoursSinceWeeklyRest = null) =>
        new(new FakeDriverRepository(drivers), new FakeTripRepository(tripsByDriver ?? [], bookingTrips ?? []),
            legal ?? new FakeLegalValidationService(), new FakeLocationRepository(),
            hours: hoursSinceWeeklyRest is null ? null : new FakeDriverHoursService(hoursSinceWeeklyRest));

    [Fact]
    public async Task FindCandidates_NonCompliantDriver_IsExcluded()
    {
        var compliant = NewDriver(1, KmNorth(10, 8));
        var nonCompliant = NewDriver(2, Origin);
        var legal = new FakeLegalValidationService(nonCompliantDriverIds: [2]);

        var result = await NewSearch([compliant, nonCompliant], legal).FindCandidates(NewTrip());

        Assert.Equal([1], result.Select(c => c.Driver.Id));
    }

    [Fact]
    public async Task FindCandidates_OverlappingUnavailability_IsExcluded()
    {
        var onLeave = NewDriver(1, Origin);
        onLeave.AddUnavailability(new DriverUnavailability(1, new TimeWindow(TripStart.AddHours(-1), TripStart.AddHours(1)), UnavailabilityType.SickLeave));
        var free = NewDriver(2, KmNorth(10, 30));

        var result = await NewSearch([onLeave, free]).FindCandidates(NewTrip());

        Assert.Equal([2], result.Select(c => c.Driver.Id));
    }

    [Fact]
    public async Task FindCandidates_NotAutoAssignableOrExternal_AreExcluded()
    {
        var noAuto = NewDriver(1, Origin, allowsAuto: false);
        var external = NewDriver(2, Origin, external: true);
        var ok = NewDriver(3, Origin);

        var result = await NewSearch([noAuto, external, ok]).FindCandidates(NewTrip());

        Assert.Equal([3], result.Select(c => c.Driver.Id));
    }

    [Fact]
    public async Task FindCandidates_ScheduleConflict_IsExcluded()
    {
        var busy = NewDriver(1, Origin);
        var other = new Trip(Origin, Destination, new TimeWindow(TripStart.AddHours(1), TripStart.AddHours(2)), 10, ServiceType.Regular);
        SetId(other, 200);

        var result = await NewSearch([busy], tripsByDriver: new() { [1] = [other] }).FindCandidates(NewTrip());

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindCandidates_CancelledTripDoesNotConflict()
    {
        var driver = NewDriver(1, Origin);
        var cancelled = new Trip(Origin, Destination, new TimeWindow(TripStart.AddHours(1), TripStart.AddHours(2)), 10, ServiceType.Regular);
        SetId(cancelled, 200);
        cancelled.Cancel();

        var result = await NewSearch([driver], tripsByDriver: new() { [1] = [cancelled] }).FindCandidates(NewTrip());

        Assert.Single(result);
    }

    [Fact]
    public async Task FindCandidates_InsufficientDailyAvailability_IsExcluded()
    {
        // Earlier work the same day 00:00-02:00 + trip until 11:00 = 11h span: fits 13h.
        // Work 18:00-20:00 → span 00:00→20:00 = 20h > 13h: no availability (SPEC 0030).
        var driver = NewDriver(1, Origin);
        var early = new Trip(Origin, Destination, new TimeWindow(TripStart.Date, TripStart.Date.AddHours(1)), 10, ServiceType.Regular);
        var late = new Trip(Origin, Destination, new TimeWindow(TripStart.Date.AddHours(18), TripStart.Date.AddHours(20)), 10, ServiceType.Regular);
        SetId(early, 201);
        SetId(late, 202);

        var result = await NewSearch([driver], tripsByDriver: new() { [1] = [early, late] }).FindCandidates(NewTrip());

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindCandidates_CurrentlyAssignedDriver_IsExcluded()
    {
        var current = NewDriver(1, Origin);
        var replacement = NewDriver(2, Origin);
        var trip = NewTrip();
        trip.AssignResources(current, new Vehicle("1234ABC", "Irizar", "i6", 50));

        var result = await NewSearch([current, replacement]).FindCandidates(trip);

        Assert.Equal([2], result.Select(c => c.Driver.Id));
    }

    [Fact]
    public async Task FindCandidates_RanksByBaseMatchThenProximity()
    {
        var noBase = NewDriver(1, null);
        var far = NewDriver(2, KmNorth(20, 60));
        var at15 = NewDriver(3, KmNorth(21, 15));
        var at3 = NewDriver(4, KmNorth(22, 3));
        var baseOrigin = NewDriver(5, Origin);
        var baseOriginAndDest = NewDriver(6, Origin);
        var trip = NewTrip(destination: Origin); // Round trip from the origin base.

        var result = await NewSearch([noBase, far, at15, at3, baseOrigin, baseOriginAndDest]).FindCandidates(trip, maxResults: 10);

        // With destination = origin, both base-at-origin drivers tie on the top tier: Id breaks the tie.
        Assert.Equal([5, 6, 4, 3, 2, 1], result.Select(c => c.Driver.Id));
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Select(c => c.Rank));
        Assert.Equal(CandidateProximity.BaseAtOriginAndDestination, result[0].Proximity);
        Assert.Equal(CandidateProximity.Within5Km, result[2].Proximity);
        Assert.Equal(CandidateProximity.Within20Km, result[3].Proximity);
        Assert.Equal(CandidateProximity.Beyond40Km, result[4].Proximity);
        Assert.Equal(CandidateProximity.NoHomeLocation, result[5].Proximity);
        Assert.Null(result[5].DistanceKm);
        Assert.All(result, c => Assert.False(string.IsNullOrWhiteSpace(c.Reason.Es())));
    }

    [Fact]
    public async Task FindCandidates_BaseAtOriginRanksBeforeCloserNonBase()
    {
        var baseOrigin = NewDriver(1, Origin);
        var near = NewDriver(2, KmNorth(10, 0.5));

        var result = await NewSearch([near, baseOrigin]).FindCandidates(NewTrip());

        Assert.Equal([1, 2], result.Select(c => c.Driver.Id));
        Assert.Equal(CandidateProximity.BaseAtOrigin, result[0].Proximity);
    }

    /// <summary>Booking 7 with an outbound trip (04:00–06:00) driven by <paramref name="outboundDriver"/> and the return trip (the one searched).</summary>
    private static (Trip Outbound, Trip Return) Excursion(Driver outboundDriver, bool cancelOutbound = false)
    {
        var booking = new Booking("Colegio San José", "Excursión ida y vuelta");
        SetId(booking, 7);
        var outbound = new Trip(Destination, Origin, new TimeWindow(TripStart.AddHours(-4), TripStart.AddHours(-2)), 10, ServiceType.Discretionary, "Ida", booking);
        SetId(outbound, 99);
        outbound.AssignResources(outboundDriver, new Vehicle("1234ABC", "Irizar", "i6", 55));
        if (cancelOutbound) outbound.Cancel();
        var ret = new Trip(Origin, Destination, new TimeWindow(TripStart, TripStart.AddHours(3)), 10, ServiceType.Discretionary, "Vuelta", booking);
        SetId(ret, 100);
        return (outbound, ret);
    }

    [Fact]
    public async Task FindCandidates_DriverOfSameBooking_RanksFirstAheadOfCloserDriver()
    {
        var sameBooking = NewDriver(1, KmNorth(10, 30));
        var baseOrigin = NewDriver(2, Origin);
        var (outbound, ret) = Excursion(sameBooking);

        var result = await NewSearch([baseOrigin, sameBooking], tripsByDriver: new() { [1] = [outbound] }, bookingTrips: [outbound, ret])
            .FindCandidates(ret);

        Assert.Equal([1, 2], result.Select(c => c.Driver.Id));
        Assert.True(result[0].CoversSameBooking);
        Assert.False(result[1].CoversSameBooking);
        Assert.Equal(CandidateProximity.Within40Km, result[0].Proximity); // Proximity is still reported, not altered.
        Assert.StartsWith("Misma reserva: ya cubre el viaje #99; ", result[0].Reason.Es()); // Short, verifiable facts.
        Assert.Equal(99, result[0].SameBookingTripId);
        Assert.Null(result[1].SameBookingTripId);
        Assert.StartsWith("Base = salida (", result[1].Reason.Es());
    }

    [Fact]
    public async Task FindCandidates_DriverOfSameBookingNotCompliant_IsNotSuggestedAndOthersStillAre()
    {
        var sameBooking = NewDriver(1, KmNorth(10, 30));
        var baseOrigin = NewDriver(2, Origin);
        var (outbound, ret) = Excursion(sameBooking);
        var legal = new FakeLegalValidationService(nonCompliantDriverIds: [1]);

        var result = await NewSearch([baseOrigin, sameBooking], legal, new() { [1] = [outbound] }, [outbound, ret]).FindCandidates(ret);

        Assert.Equal([2], result.Select(c => c.Driver.Id));
        Assert.False(result[0].CoversSameBooking);
    }

    [Fact]
    public async Task FindCandidates_CancelledTripOfSameBooking_GivesNoPreference()
    {
        var sameBooking = NewDriver(1, KmNorth(10, 30));
        var baseOrigin = NewDriver(2, Origin);
        var (outbound, ret) = Excursion(sameBooking, cancelOutbound: true);

        var result = await NewSearch([baseOrigin, sameBooking], bookingTrips: [outbound, ret]).FindCandidates(ret);

        Assert.Equal([2, 1], result.Select(c => c.Driver.Id));
        Assert.All(result, c => Assert.False(c.CoversSameBooking));
    }

    [Fact]
    public async Task FindCandidates_SameBookingPreference_IsExplained()
    {
        var sameBooking = NewDriver(1, KmNorth(10, 30));
        var baseOrigin = NewDriver(2, Origin);
        var (outbound, ret) = Excursion(sameBooking);

        var result = await NewSearch([baseOrigin, sameBooking], tripsByDriver: new() { [1] = [outbound] }, bookingTrips: [outbound, ret])
            .FindCandidates(ret);
        var explanations = DispatchGuard.Application.Services.Explanations.DecisionExplainer.ForDriverCandidates(result);

        Assert.Contains(explanations[0].Business, i => i.Title.Es() == "Misma reserva" && i.Outcome == ExplanationOutcome.Passed);
        Assert.Contains(explanations[0].Flow, s => s.Result.Es().Contains("reserva"));
        Assert.Contains("reserva", explanations[0].Summary.Es());
        Assert.DoesNotContain(explanations[1].Business, i => i.Title.Es() == "Misma reserva");
    }

    [Fact]
    public async Task FindCandidates_ReturnsAtMostN_DefaultThree()
    {
        var drivers = Enumerable.Range(1, 5).Select(i => NewDriver(i, KmNorth(10 + i, i))).ToList();

        var result = await NewSearch(drivers).FindCandidates(NewTrip());

        Assert.Equal([1, 2, 3], result.Select(c => c.Driver.Id));
    }

    [Fact]
    public async Task FindCandidates_NoEligibleDrivers_ReturnsEmptyList()
    {
        var legal = new FakeLegalValidationService(nonCompliantDriverIds: [1]);

        var result = await NewSearch([NewDriver(1, Origin)], legal).FindCandidates(NewTrip());

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindCandidates_EvaluatesComplianceOnlyUntilNFound()
    {
        var drivers = Enumerable.Range(1, 6).Select(i => NewDriver(i, KmNorth(10 + i, i))).ToList();
        var legal = new FakeLegalValidationService(nonCompliantDriverIds: [1]);

        var result = await NewSearch(drivers, legal).FindCandidates(NewTrip(), maxResults: 2);

        Assert.Equal([2, 3], result.Select(c => c.Driver.Id));
        Assert.Equal([1, 2, 3], legal.EvaluatedDriverIds);
    }

    [Fact]
    public async Task FindCandidates_WarningsAreCarried()
    {
        var legal = new FakeLegalValidationService(warnDriverIds: [1]);

        var result = await NewSearch([NewDriver(1, Origin)], legal).FindCandidates(NewTrip());

        Assert.Single(Assert.Single(result).Warnings);
    }

    [Fact]
    public async Task FindCandidates_MaxResultsBelowOne_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NewSearch([]).FindCandidates(NewTrip(), maxResults: 0));
    }

    [Fact]
    public async Task FindCandidates_TiedDrivers_FewerHoursSinceWeeklyRestFirst()
    {
        var first = NewDriver(1, Origin);
        var second = NewDriver(2, Origin);

        var result = await NewSearch([first, second], hoursSinceWeeklyRest: new() { [1] = 20, [2] = 12 }).FindCandidates(NewTrip());

        Assert.Equal([2, 1], result.Select(c => c.Driver.Id));
        Assert.Equal(TimeSpan.FromHours(12), result[0].HoursSinceWeeklyRest);
        Assert.True(result[0].WonTieBreak);
    }

    [Fact]
    public async Task FindCandidates_SameHours_IdStillBreaksTheTie()
    {
        var result = await NewSearch([NewDriver(2, Origin), NewDriver(1, Origin)], hoursSinceWeeklyRest: new() { [1] = 12, [2] = 12 })
            .FindCandidates(NewTrip());

        Assert.Equal([1, 2], result.Select(c => c.Driver.Id));
        Assert.False(result[0].WonTieBreak);
    }

    [Fact]
    public async Task FindCandidates_HoursNeverBeatProximity()
    {
        var closeButTired = NewDriver(1, Origin);
        var farButRested = NewDriver(2, KmNorth(10, 15));

        var result = await NewSearch([closeButTired, farButRested], hoursSinceWeeklyRest: new() { [1] = 50, [2] = 0 })
            .FindCandidates(NewTrip());

        Assert.Equal([1, 2], result.Select(c => c.Driver.Id));
        Assert.False(result[0].WonTieBreak);
    }

    [Fact]
    public async Task FindCandidates_TieBreakIsNotAFilter_TiredDriverStillListed()
    {
        var result = await NewSearch([NewDriver(1, Origin), NewDriver(2, Origin)], hoursSinceWeeklyRest: new() { [1] = 55, [2] = 1 })
            .FindCandidates(NewTrip());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindCandidates_TieBreak_IsExplained()
    {
        var result = await NewSearch([NewDriver(1, Origin), NewDriver(2, Origin)], hoursSinceWeeklyRest: new() { [1] = 20, [2] = 12 })
            .FindCandidates(NewTrip());
        var explanations = DispatchGuard.Application.Services.Explanations.DecisionExplainer.ForDriverCandidates(result);

        var item = Assert.Single(explanations[0].Business, i => i.Title.Es() == "Desempate por horas");
        Assert.Equal("CE 561/2006 Art. 8.6", item.Reference);
        Assert.Contains("12", item.Detail.Es());
        Assert.DoesNotContain(explanations[1].Business, i => i.Title.Es() == "Desempate por horas");
    }

    [Fact]
    public async Task FindOptions_ReturnsLegalAndDiscardedWithFirstFailingFilter()
    {
        var ok = NewDriver(1, Origin);
        var illegal = NewDriver(2, Origin);
        var external = NewDriver(3, Origin, external: true);
        var busy = NewDriver(4, Origin);
        var other = new Trip(Origin, Destination, new TimeWindow(TripStart.AddHours(1), TripStart.AddHours(2)), 10, ServiceType.Regular);
        SetId(other, 200);

        var outcome = await NewSearch([ok, illegal, external, busy], new FakeLegalValidationService(nonCompliantDriverIds: [2]),
            tripsByDriver: new() { [4] = [other] }).FindOptions(NewTrip());

        Assert.Equal([1], outcome.Candidates.Select(c => c.Driver.Id));
        Assert.Equal([2, 3, 4], outcome.Discarded.Select(d => d.Driver.Id).Order());
        Assert.Contains("CE 561", outcome.Discarded.Single(d => d.Driver.Id == 2).Reason.Es());
        Assert.Contains("externo", outcome.Discarded.Single(d => d.Driver.Id == 3).Reason.Es());
        Assert.Contains("solape", outcome.Discarded.Single(d => d.Driver.Id == 4).Reason.Es());
    }

    private sealed class FakeLegalValidationService(int[]? nonCompliantDriverIds = null, int[]? warnDriverIds = null) : ILegalValidationService
    {
        public List<int> EvaluatedDriverIds { get; } = [];

        public async Task<LegalValidation> ValidateDriverForTrip(Driver driver, Trip trip, CancellationToken cancellationToken = default) =>
            (await ValidateDriverForTripDetailed(driver, trip, cancellationToken)).ToSnapshot();

        public Task<ComplianceCheckResult> ValidateDriverForTripDetailed(Driver driver, Trip trip, CancellationToken cancellationToken = default)
        {
            EvaluatedDriverIds.Add(driver.Id);
            if (nonCompliantDriverIds?.Contains(driver.Id) == true)
            {
                return Task.FromResult(new ComplianceCheckResult(false, [ComplianceViolation.Critical("TEST", Texts.AsWritten("not compliant"))], TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
            }

            return Task.FromResult(warnDriverIds?.Contains(driver.Id) == true
                ? ComplianceCheckResult.CompliantWithWarnings([ComplianceViolation.Warning("TEST", Texts.AsWritten("warn"))], TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90))
                : ComplianceCheckResult.Compliant(TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90)));
        }
    }

    private sealed class FakeDriverRepository(IEnumerable<Driver> drivers) : IDriverRepository
    {
        private readonly List<Driver> _drivers = drivers.ToList();
        public Task<IEnumerable<Driver>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Driver>>(_drivers.Where(d => d.IsActive));
        public Task<Driver?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(_drivers.FirstOrDefault(d => d.Id == id));
        public Task<bool> ExistsByIdentificationNumberAsync(string identificationNumber, int? exceptId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Driver> AddAsync(Driver entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Driver entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Driver entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    /// <summary>Locations are already loaded as navigations in these tests: lookups must not be needed.</summary>
    private sealed class FakeLocationRepository : ILocationRepository
    {
        public Task<Location?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Location>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Location> AddAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Location entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeTripRepository(Dictionary<int, List<Trip>> tripsByDriver, IEnumerable<Trip> bookingTrips) : ITripRepository
    {
        public Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Trip>>([]);
        public Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Trip>>(bookingTrips.Where(t => t.BookingId == bookingId).ToList());

        public Task<PagedResult<Trip>> GetByDriverAsync(int driverId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default)
        {
            var items = tripsByDriver.GetValueOrDefault(driverId, [])
                .Where(t => t.Id != excludeTripId && (to is null || t.ScheduledWindow.Start < to) && (from is null || t.ScheduledWindow.End > from))
                .ToList();
            return Task.FromResult(PagedResult<Trip>.Create(items, items.Count, page, pageSize));
        }

        public Task<PagedResult<Trip>> GetByVehicleAsync(int vehicleId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Trip>> GetByDateRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<PagedResult<Trip>> GetByStatusAsync(TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Trip?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Trip>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Trip> AddAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeDriverHoursService(Dictionary<int, double> hours) : IDriverHoursService
    {
        public Task<DriverHours> MeasureAsync(int driverId, DateTime asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(DriverHours.Of(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromHours(hours.GetValueOrDefault(driverId)), null));
    }
}
