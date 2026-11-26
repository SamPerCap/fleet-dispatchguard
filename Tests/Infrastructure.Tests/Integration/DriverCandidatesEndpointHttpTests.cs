using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>The database is shared by the collection, so assertions only look at the drivers seeded here.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DriverCandidatesEndpointHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DriverCandidatesEndpointHttpTests(PostgreSqlFixture db)
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
    public async Task DriverCandidates_ExcludesOnLeaveAndBusy_RanksBaseThenDistanceThenNoBase()
    {
        var start = new DateTime(2036, 2, 3, 8, 0, 0, DateTimeKind.Utc);
        int tripId, atBase, at12Km, noBase, onLeave, busy;
        await using (var write = _db.CreateContext())
        {
            var origin = Loc("Origen", 40.4168m);
            var destination = Loc("Destino", 40.6000m);
            var near = Loc("Cerca", 40.4168m + 0.108m); // ~12 km north
            var vehicle = TripRepositoryTests.NewVehicle();
            var dBase = TripRepositoryTests.NewDriver();
            var d12 = TripRepositoryTests.NewDriver();
            var dNone = TripRepositoryTests.NewDriver();
            var dLeave = TripRepositoryTests.NewDriver();
            var dBusy = TripRepositoryTests.NewDriver();
            write.AddRange(origin, destination, near, vehicle, dBase, d12, dNone, dLeave, dBusy);
            await write.SaveChangesAsync();

            dBase.SetHomeLocation(origin);
            d12.SetHomeLocation(near);
            dLeave.SetHomeLocation(origin);
            dBusy.SetHomeLocation(origin);
            dLeave.AddUnavailability(new DriverUnavailability(dLeave.Id, new TimeWindow(start.AddHours(-2), start.AddHours(2)), UnavailabilityType.SickLeave));
            var other = new Trip(origin, destination, new TimeWindow(start.AddHours(1), start.AddHours(3)), 10, ServiceType.Regular);
            other.AssignResources(dBusy, vehicle);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(4)), 10, ServiceType.Regular);
            write.Trips.AddRange(other, trip);
            await write.SaveChangesAsync();
            (tripId, atBase, at12Km, noBase, onLeave, busy) = (trip.Id, dBase.Id, d12.Id, dNone.Id, dLeave.Id, dBusy.Id);
        }

        var response = await _client.GetAsync($"/api/trips/{tripId}/driver-candidates?maxResults=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("DriverId").GetInt32()).ToList();
        Assert.DoesNotContain(onLeave, ids);
        Assert.DoesNotContain(busy, ids);
        Assert.True(ids.IndexOf(atBase) < ids.IndexOf(at12Km) && ids.IndexOf(at12Km) < ids.IndexOf(noBase));
        Assert.True(ids.IndexOf(atBase) >= 0);

        var first = items[ids.IndexOf(atBase)];
        Assert.Equal("BaseAtOrigin", first.GetProperty("Proximity").GetString());
        var mid = items[ids.IndexOf(at12Km)];
        Assert.Equal("Within20Km", mid.GetProperty("Proximity").GetString());
        Assert.InRange(mid.GetProperty("DistanceKm").GetDouble(), 11.5, 12.5);
        Assert.Equal("NoHomeLocation", items[ids.IndexOf(noBase)].GetProperty("Proximity").GetString());
        // No driving history: compliant, with the warning of SPEC 0040 carried to the dispatcher.
        Assert.NotEmpty(first.GetProperty("Warnings").EnumerateArray());

        // Each candidate explains its band, its figures and why it is where it is.
        var midBusiness = mid.GetProperty("Explanation").GetProperty("Business").EnumerateArray().ToList();
        Assert.Contains(midBusiness, i => i.GetProperty("Detail").GetString()!.Contains("radio de 20 km"));
        Assert.Contains(midBusiness, i => i.GetProperty("Reference").GetString() == "Art. 6.1");
        Assert.Contains(midBusiness, i => i.GetProperty("Title").GetString()!.StartsWith("Posición", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DriverCandidates_DefaultReturnsAtMostThree()
    {
        var tripId = await SeedTripAsync(new DateTime(2036, 3, 3, 8, 0, 0, DateTimeKind.Utc));

        var response = await _client.GetAsync($"/api/trips/{tripId}/driver-candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.InRange(items.GetArrayLength(), 0, 3);
    }

    [Fact]
    public async Task DriverCandidates_UnknownTrip_Returns404Problem()
    {
        var response = await _client.GetAsync("/api/trips/999999/driver-candidates");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DriverCandidates_MaxResultsZero_Returns400Problem()
    {
        var tripId = await SeedTripAsync(new DateTime(2036, 4, 3, 8, 0, 0, DateTimeKind.Utc));

        var response = await _client.GetAsync($"/api/trips/{tripId}/driver-candidates?maxResults=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DriverOptions_ListsLegalAndDiscardedWithReason_AndHoursBreakTies()
    {
        // Two drivers at the same base; the one who drove earlier today ranks second (SPEC 0047).
        var start = new DateTime(2036, 5, 6, 14, 0, 0, DateTimeKind.Utc);
        int tripId, rested, tired, onLeave;
        await using (var write = _db.CreateContext())
        {
            var origin = Loc("Origen", 40.4168m);
            var destination = Loc("Destino", 40.6000m);
            var vehicle = TripRepositoryTests.NewVehicle();
            var dRested = TripRepositoryTests.NewDriver();
            var dTired = TripRepositoryTests.NewDriver();
            var dLeave = TripRepositoryTests.NewDriver();
            write.AddRange(origin, destination, vehicle, dTired, dRested, dLeave);
            await write.SaveChangesAsync();
            Assert.True(dTired.Id < dRested.Id); // so only the hours (not the Id) can put the rested driver first
            await write.SaveChangesAsync();

            dRested.SetHomeLocation(origin);
            dTired.SetHomeLocation(origin);
            dLeave.SetHomeLocation(origin);
            dLeave.AddUnavailability(new DriverUnavailability(dLeave.Id, new TimeWindow(start.AddHours(-1), start.AddHours(5)), UnavailabilityType.SickLeave));
            var morning = new Trip(origin, destination, new TimeWindow(start.AddHours(-6), start.AddHours(-3)), 10, ServiceType.Regular);
            morning.AssignResources(dTired, vehicle);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(2)), 10, ServiceType.Regular);
            write.Trips.AddRange(morning, trip);
            await write.SaveChangesAsync();
            (tripId, rested, tired, onLeave) = (trip.Id, dRested.Id, dTired.Id, dLeave.Id);
        }

        var options = JsonDocument.Parse(await (await _client.GetAsync($"/api/trips/{tripId}/driver-options")).Content.ReadAsStringAsync()).RootElement;
        var ids = options.GetProperty("Candidates").EnumerateArray().Select(c => c.GetProperty("DriverId").GetInt32()).ToList();
        Assert.True(ids.IndexOf(rested) >= 0 && ids.IndexOf(rested) < ids.IndexOf(tired));
        Assert.True(options.GetProperty("Candidates")[ids.IndexOf(rested)].GetProperty("WonTieBreak").GetBoolean());
        var leave = options.GetProperty("Discarded").EnumerateArray().Single(d => d.GetProperty("DriverId").GetInt32() == onLeave);
        Assert.Contains("Conductor no disponible (", leave.GetProperty("Reason").GetString());

        var hours = JsonDocument.Parse(await (await _client.GetAsync($"/api/drivers/{tired}/hours?at={start:yyyy-MM-ddTHH:mm:ss}Z")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, hours.GetProperty("Today").GetDouble());
        Assert.Equal(3, hours.GetProperty("ThisWeek").GetDouble());
        Assert.Equal(9, hours.GetProperty("MaxDaily").GetDouble());
        Assert.Equal(56, hours.GetProperty("MaxWeekly").GetDouble());
        Assert.Equal(90, hours.GetProperty("MaxBiWeekly").GetDouble());
    }

    [Fact]
    public async Task DriverHours_UnknownDriver_Returns404()
    {
        var response = await _client.GetAsync("/api/drivers/999999/hours");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<int> SeedTripAsync(DateTime start)
    {
        await using var write = _db.CreateContext();
        var origin = Loc("Origen", 40.4168m);
        var destination = Loc("Destino", 40.6000m);
        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(2)), 10, ServiceType.Regular);
        write.AddRange(origin, destination, trip, TripRepositoryTests.NewDriver());
        await write.SaveChangesAsync();
        return trip.Id;
    }

    private static Location Loc(string name, decimal lat) =>
        new(name, $"{name} 1", new GeoCoordinate(lat, -3.7038m), "Madrid", "Madrid");
}
