using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class VehicleChainingEndpointHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public VehicleChainingEndpointHttpTests(PostgreSqlFixture db)
    {
        _db = db;
        _factory = new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Propose_TwoVehiclesThatCanChain_ProposesChangeWithMetricsAndExplanation_WithoutPersisting()
    {
        var day = new DateTime(2037, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        int secondTripId, firstVehicleId, secondVehicleId;
        await using (var write = _db.CreateContext())
        {
            var placeA = new Location("Chain A", "A 1", new GeoCoordinate(40.0000m, -3.7000m), "Madrid", "Madrid");
            var placeB = new Location("Chain B", "B 1", new GeoCoordinate(40.4497m, -3.7000m), "Madrid", "Madrid");  // ≈50 km
            var placeC = new Location("Chain C", "C 1", new GeoCoordinate(40.8993m, -3.7000m), "Madrid", "Madrid");  // ≈100 km
            var v1 = TripRepositoryTests.NewVehicle();
            var v2 = TripRepositoryTests.NewVehicle();
            var d1 = TripRepositoryTests.NewDriver();
            var d2 = TripRepositoryTests.NewDriver();
            write.AddRange(placeA, placeB, placeC, v1, v2, d1, d2);
            await write.SaveChangesAsync();

            v1.SetHomeLocation(placeA);
            v2.SetHomeLocation(placeC);
            var t1 = new Trip(placeA, placeB, new TimeWindow(day.AddHours(8), day.AddHours(9)), 10, ServiceType.Regular);
            t1.AssignResources(d1, v1);
            var t2 = new Trip(placeB, placeA, new TimeWindow(day.AddHours(12), day.AddHours(13)), 10, ServiceType.Regular);
            t2.AssignResources(d2, v2);
            write.Trips.AddRange(t1, t2);
            await write.SaveChangesAsync();
            (secondTripId, firstVehicleId, secondVehicleId) = (t2.Id, v1.Id, v2.Id);
        }

        var json = JsonDocument.Parse(await _client.GetStringAsync(
            $"/api/optimization/vehicle-chaining?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}&returnToBase=true")).RootElement;

        Assert.Equal(70, json.GetProperty("AverageSpeedKmh").GetDouble());
        Assert.True(json.GetProperty("ProposedDeadheadKm").GetDouble() < json.GetProperty("BaselineDeadheadKm").GetDouble());
        var change = json.GetProperty("Changes").EnumerateArray().Single(c => c.GetProperty("TripId").GetInt32() == secondTripId);
        Assert.Equal(secondVehicleId, change.GetProperty("CurrentVehicleId").GetInt32());
        Assert.Equal(firstVehicleId, change.GetProperty("ProposedVehicleId").GetInt32());
        Assert.NotEmpty(change.GetProperty("Explanation").GetProperty("Business").EnumerateArray());

        // Read-only: the trip keeps its vehicle.
        var trip = JsonDocument.Parse(await _client.GetStringAsync($"/api/trips/{secondTripId}")).RootElement;
        Assert.Equal(secondVehicleId, trip.GetProperty("AssignedVehicleId").GetInt32());
    }

    /// <summary>
    /// With a fixed clock, both the report and the Control Center only propose what has not departed (SPEC 0070). The clock carries
    /// the Madrid offset: 13:30+01:00 is 12:30Z, so #2 (12:45Z) has not departed although the wall clock says later than 12:45.
    /// </summary>
    [Theory]
    [InlineData(0, -30 * 24 * 60, 0, true)] // a month before: future
    [InlineData(1, 8 * 60, 1, true)]        // #1 leaves right now
    [InlineData(2, 12 * 60 + 30, 1, true)]  // #1 finished, #2 about to leave
    [InlineData(3, 12 * 60 + 45, 2, false)] // #2 leaves right now
    [InlineData(4, 9 * 24 * 60, 2, false)]  // past
    public async Task Propose_AtEachMoment_KeepsDepartedTrips_InReportAndControlCenter(int caseNumber, int minutes, int departed, bool changeable)
    {
        // One day per case, so that they do not see each other's trips (the database is shared by the collection).
        var day = new DateTime(2046, 2, 5, 0, 0, 0, DateTimeKind.Utc).AddDays(caseNumber);
        int firstId, secondId, baseId;
        await using (var write = _db.CreateContext())
        {
            var operatingBase = new OperatingBase($"S-{Guid.NewGuid():N}"[..20]);
            write.Add(operatingBase);
            await write.SaveChangesAsync();
            var placeA = new Location("Salida A", "A 1", new GeoCoordinate(40.0000m, -3.7000m), "Madrid", "Madrid");
            var placeB = new Location("Salida B", "B 1", new GeoCoordinate(40.4497m, -3.7000m), "Madrid", "Madrid");  // ≈50 km
            var placeC = new Location("Salida C", "C 1", new GeoCoordinate(40.8993m, -3.7000m), "Madrid", "Madrid");  // ≈100 km
            var v1 = TripRepositoryTests.NewVehicle();
            var v2 = TripRepositoryTests.NewVehicle();
            var d1 = TripRepositoryTests.NewDriver();
            var d2 = TripRepositoryTests.NewDriver();
            foreach (var vehicle in new[] { v1, v2 }) vehicle.MoveToBase(operatingBase.Id);
            foreach (var driver in new[] { d1, d2 }) driver.MoveToBase(operatingBase.Id);
            write.AddRange(placeA, placeB, placeC, v1, v2, d1, d2);
            await write.SaveChangesAsync();

            v1.SetHomeLocation(placeA);
            v2.SetHomeLocation(placeC);
            var t1 = new Trip(placeA, placeB, new TimeWindow(day.AddHours(8), day.AddHours(9)), 10, ServiceType.Regular);
            t1.AssignResources(d1, v1);
            var t2 = new Trip(placeB, placeA, new TimeWindow(day.AddHours(12.75), day.AddHours(14)), 10, ServiceType.Regular);
            t2.AssignResources(d2, v2);
            foreach (var trip in new[] { t1, t2 }) trip.MoveToBase(operatingBase.Id);
            write.Trips.AddRange(t1, t2);
            await write.SaveChangesAsync();
            (firstId, secondId, baseId) = (t1.Id, t2.Id, operatingBase.Id);
        }

        var now = new DateTimeOffset(day.AddMinutes(minutes)).ToOffset(TimeSpan.FromHours(1));
        using var factory = _factory.WithWebHostBuilder(x => x.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        using var client = factory.CreateClient();

        var report = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/optimization/vehicle-chaining?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}")).RootElement;
        Assert.Equal(2, report.GetProperty("TripsConsidered").GetInt32());
        Assert.Equal(departed, report.GetProperty("TripsDeparted").GetInt32());
        var changed = report.GetProperty("Changes").EnumerateArray().Select(x => x.GetProperty("TripId").GetInt32()).ToList();
        Assert.DoesNotContain(changed, id => departed >= 1 && id == firstId);
        Assert.Equal(changeable, changed.Contains(secondId));
        if (!changeable)
        {
            Assert.Equal(report.GetProperty("BaselineDeadheadKm").GetDouble(), report.GetProperty("ProposedDeadheadKm").GetDouble());
            Assert.Contains("ya han salido", report.GetProperty("Explanation").GetProperty("Summary").GetString());
        }

        // Same rule in the Control Center (only the vehicles of the base): 50 back + 50 to go + 100 back = 200 km now.
        var efficiency = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/control/efficiency?baseId={baseId}&date={Uri.EscapeDataString(day.AddHours(10).ToString("o"))}")).RootElement;
        Assert.Equal(departed, efficiency.GetProperty("TripsDeparted").GetInt32());
        Assert.Equal(changeable ? 1 : 0, efficiency.GetProperty("ProposedChanges").GetInt32());
        Assert.Equal(200, efficiency.GetProperty("BaselineDeadheadKm").GetDouble(), 0.5);
        Assert.Equal(changeable ? 0 : 200, efficiency.GetProperty("ProposedDeadheadKm").GetDouble(), 0.5);
        Assert.Equal(changeable ? 100 : 0, efficiency.GetProperty("SavingPercent").GetDouble(), 0.5);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The week of a date range is Madrid's, also across both clock changes; instants are still instants.</summary>
    [Theory]
    [InlineData("2046-11-05", "2046-11-12", "2046-11-04T23:00:00Z", "2046-11-11T23:00:00Z")] // winter
    [InlineData("2046-07-02", "2046-07-09", "2046-07-01T22:00:00Z", "2046-07-08T22:00:00Z")] // summer
    [InlineData("2046-03-19", "2046-03-26", "2046-03-18T23:00:00Z", "2046-03-25T22:00:00Z")] // spring change on Sunday 25
    [InlineData("2046-10-22", "2046-10-29", "2046-10-21T22:00:00Z", "2046-10-28T23:00:00Z")] // autumn change on Sunday 28
    public async Task Propose_Week_IsCutAtMadridMidnight(string from, string to, string fromUtc, string toUtc)
    {
        var start = DateTime.Parse(fromUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var end = DateTime.Parse(toUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var half = TimeSpan.FromMinutes(30);
        // Monday 00:30 and 23:30, Sunday 00:30 and 23:30 of the week; the Sunday 23:30 before and the Monday 00:30 after are not.
        DateTime[] inside = [start + half, start.AddDays(1) - half, start.AddDays(6) + half, end - half];
        DateTime[] outside = [start - half, end + half];
        await using (var write = _db.CreateContext())
        {
            var placeA = new Location("Semana A", "A 1", new GeoCoordinate(28.0000m, -15.5000m), "Madrid", "Madrid");
            write.Add(placeA);
            foreach (var at in inside.Concat(outside))
            {
                var vehicle = TripRepositoryTests.NewVehicle();
                var driver = TripRepositoryTests.NewDriver();
                write.AddRange(vehicle, driver);
                await write.SaveChangesAsync();
                vehicle.SetHomeLocation(placeA); // far from the other tests of the collection, which propose vehicles of the whole fleet
                var trip = new Trip(placeA, placeA, new TimeWindow(at, at.AddMinutes(20)), 10, ServiceType.Regular);
                trip.AssignResources(driver, vehicle);
                write.Trips.Add(trip);
            }

            await write.SaveChangesAsync();
        }

        async Task<JsonElement> Report(string reportFrom, string reportTo) => JsonDocument.Parse(await _client.GetStringAsync(
            $"/api/optimization/vehicle-chaining?from={Uri.EscapeDataString(reportFrom)}&to={Uri.EscapeDataString(reportTo)}")).RootElement;

        var byDates = await Report(from, to);
        Assert.Equal(inside.Length, byDates.GetProperty("TripsConsidered").GetInt32());
        Assert.Equal((start, end), (byDates.GetProperty("From").GetDateTime(), byDates.GetProperty("To").GetDateTime()));

        var byInstants = await Report(fromUtc, toUtc);
        Assert.Equal(inside.Length, byInstants.GetProperty("TripsConsidered").GetInt32());
        Assert.Equal((start, end), (byInstants.GetProperty("From").GetDateTime(), byInstants.GetProperty("To").GetDateTime()));
    }

    [Fact]
    public async Task Propose_OctoberWholeMonth_IsStill31Days() =>
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/optimization/vehicle-chaining?from=2037-10-01&to=2037-11-01")).StatusCode);

    /// <summary>The Control Center's "today" at 00:30 and 23:30 Madrid, also on the days of the clock changes.</summary>
    [Theory]
    [InlineData("2046-12-03T23:00:00Z", "2046-12-04T23:00:00Z")] // Tuesday, winter
    [InlineData("2046-08-05T22:00:00Z", "2046-08-06T22:00:00Z")] // Monday, summer
    [InlineData("2047-03-30T23:00:00Z", "2047-03-31T22:00:00Z")] // 23 hours
    [InlineData("2047-10-26T22:00:00Z", "2047-10-27T23:00:00Z")] // 25 hours
    public async Task ControlCenter_Today_IsTheMadridDay_AndOneReturnToBase(string fromUtc, string toUtc)
    {
        var start = DateTime.Parse(fromUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var end = DateTime.Parse(toUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var half = TimeSpan.FromMinutes(30);
        int baseId;
        await using (var write = _db.CreateContext())
        {
            var operatingBase = new OperatingBase($"H-{Guid.NewGuid():N}"[..20]);
            var placeA = new Location("Hoy A", "A 1", new GeoCoordinate(28.0000m, -15.5000m), "Madrid", "Madrid");
            var placeB = new Location("Hoy B", "B 1", new GeoCoordinate(28.4497m, -15.5000m), "Madrid", "Madrid"); // ≈50 km
            var vehicle = TripRepositoryTests.NewVehicle();
            var driver = TripRepositoryTests.NewDriver();
            write.Add(operatingBase);
            await write.SaveChangesAsync();
            vehicle.MoveToBase(operatingBase.Id);
            driver.MoveToBase(operatingBase.Id);
            write.AddRange(placeA, placeB, vehicle, driver);
            await write.SaveChangesAsync();
            vehicle.SetHomeLocation(placeA);
            // A→B at 00:30 and B→A at 23:30 of the same Madrid day; the day before and after, nothing that counts today.
            foreach (var (at, origin, destination) in new[] { (start - half, placeA, placeA), (start + half, placeA, placeB), (end - half, placeB, placeA), (end + half, placeA, placeA) })
            {
                var trip = new Trip(origin, destination, new TimeWindow(at, at.AddMinutes(20)), 10, ServiceType.Regular);
                trip.AssignResources(driver, vehicle);
                trip.MoveToBase(operatingBase.Id);
                write.Trips.Add(trip);
            }

            await write.SaveChangesAsync();
            baseId = operatingBase.Id;
        }

        foreach (var at in new[] { start, start + half, end - half, end.AddTicks(-1) })
        {
            var efficiency = JsonDocument.Parse(await _client.GetStringAsync(
                $"/api/control/efficiency?baseId={baseId}&date={Uri.EscapeDataString(at.ToString("o"))}")).RootElement;
            Assert.Equal((start, end), (efficiency.GetProperty("From").GetDateTime(), efficiency.GetProperty("To").GetDateTime()));
            Assert.Equal(2, efficiency.GetProperty("TripsConsidered").GetInt32());
            Assert.Equal(0, efficiency.GetProperty("BaselineDeadheadKm").GetDouble(), 0.1); // the same day: no way back in between
        }
    }

    /// <summary>
    /// With a fixed clock, V1 (parked 50 km away, 43 min) is only proposed while it arrives. Its position is the last service it did, even
    /// of the day before or of another base, unless the daily return to base already moved it.
    /// </summary>
    [Theory]
    [InlineData(0, -60, 0, true)]     // at its base (next to the service): arrives at any time
    [InlineData(1, -60, 1, true)]     // its last service today left it 50 km away: an hour is enough
    [InlineData(2, -42, 1, false)]    // 42 min are not
    [InlineData(3, -42, 2, true)]     // that service was yesterday (Madrid): it went back to its base
    public async Task Propose_ImminentService_OnlyWithAVehicleThatArrivesLeavingNow(int caseNumber, int minutes, int lastService, bool arrives)
    {
        var day = new DateTime(2048, 2, 10, 0, 0, 0, DateTimeKind.Utc).AddDays(caseNumber * 7);
        var departure = day.AddHours(12);
        int tripId, v1Id, baseId, lastId = 0;
        await using (var write = _db.CreateContext())
        {
            var operatingBase = new OperatingBase($"L-{Guid.NewGuid():N}"[..20]);
            var otherBase = new OperatingBase($"L-{Guid.NewGuid():N}"[..20]);
            var placeA = new Location("Llega A", "A 1", new GeoCoordinate(28.0000m, -15.5000m), "Madrid", "Madrid");
            var placeB = new Location("Llega B", "B 1", new GeoCoordinate(28.4497m, -15.5000m), "Madrid", "Madrid");  // ≈50 km
            var placeE = new Location("Llega E", "E 1", new GeoCoordinate(30.6980m, -15.5000m), "Madrid", "Madrid");  // ≈300 km
            var v1 = TripRepositoryTests.NewVehicle();
            var v2 = TripRepositoryTests.NewVehicle();
            var d1 = TripRepositoryTests.NewDriver();
            var d2 = TripRepositoryTests.NewDriver();
            write.AddRange(operatingBase, otherBase);
            await write.SaveChangesAsync();
            foreach (var vehicle in new[] { v1, v2 }) vehicle.MoveToBase(operatingBase.Id);
            foreach (var driver in new[] { d1, d2 }) driver.MoveToBase(operatingBase.Id);
            write.AddRange(placeA, placeB, placeE, v1, v2, d1, d2);
            await write.SaveChangesAsync();
            v1.SetHomeLocation(placeB);
            v2.SetHomeLocation(placeE);
            var trip = new Trip(placeB, placeB, new TimeWindow(departure, departure.AddHours(1)), 10, ServiceType.Regular);
            trip.AssignResources(d2, v2);
            trip.MoveToBase(operatingBase.Id);
            write.Trips.Add(trip);
            if (lastService > 0)
            {
                // Two departed ones (the newest counts), of another base; plus a cancelled and a future one, which do not.
                var start = lastService == 1 ? day.AddHours(8) : day.AddHours(-6);
                var older = new Trip(placeB, placeE, new TimeWindow(start.AddDays(-2), start.AddDays(-2).AddHours(1)), 10, ServiceType.Regular);
                var last = new Trip(placeB, placeA, new TimeWindow(start, start.AddHours(1)), 10, ServiceType.Regular);
                var cancelled = new Trip(placeB, placeE, new TimeWindow(start.AddHours(1), start.AddHours(2)), 10, ServiceType.Regular);
                var future = new Trip(placeB, placeE, new TimeWindow(departure.AddDays(3), departure.AddDays(3).AddHours(1)), 10, ServiceType.Regular);
                foreach (var service in new[] { older, last, cancelled, future })
                {
                    service.AssignResources(d1, v1);
                    service.MoveToBase(otherBase.Id);
                    write.Trips.Add(service);
                }

                cancelled.Cancel();
                await write.SaveChangesAsync();
                lastId = last.Id;
            }

            await write.SaveChangesAsync();
            (tripId, v1Id, baseId) = (trip.Id, v1.Id, operatingBase.Id);
        }

        var now = departure.AddMinutes(minutes);
        using var factory = _factory.WithWebHostBuilder(x => x.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedClock(now))));
        using var client = factory.CreateClient();

        var efficiency = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/control/efficiency?baseId={baseId}&date={Uri.EscapeDataString(now.ToString("o"))}")).RootElement;
        Assert.Equal(arrives ? 1 : 0, efficiency.GetProperty("ProposedChanges").GetInt32());

        // Same period and rule in the report (the whole fleet: other vehicles of the collection may be as close, V1 must not be forced).
        var report = JsonDocument.Parse(await client.GetStringAsync(
            $"/api/optimization/vehicle-chaining?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}")).RootElement;
        Assert.DoesNotContain("no tiene ningún vehículo apto", report.GetProperty("Explanation").GetProperty("Summary").GetString());
        var proposed = report.GetProperty("Changes").EnumerateArray()
            .Where(c => c.GetProperty("TripId").GetInt32() == tripId).Select(c => c.GetProperty("ProposedVehicleId").GetInt32()).ToList();
        if (!arrives) Assert.DoesNotContain(v1Id, proposed);

        await using var read = _db.CreateContext();
        var lasts = await new DispatchGuard.Infrastructure.Repositories.VehicleLastServiceReader(read).LastServicesAsync([v1Id], now);
        Assert.Equal(lastService == 0 ? [] : [lastId], lasts.Select(t => t.Id));
    }

    [Theory]
    [InlineData("from=2037-03-02&to=2037-03-01")]
    [InlineData("from=2037-01-01&to=2037-03-01")]
    [InlineData("from=2037-10-01&to=2037-11-02")] // 32 days
    [InlineData("from=2037-03-01&to=2037-04-02")] // 32 days, one hour less
    [InlineData("to=2037-03-01")]
    public async Task Propose_InvalidRange_Returns400(string query)
    {
        var response = await _client.GetAsync($"/api/optimization/vehicle-chaining?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
