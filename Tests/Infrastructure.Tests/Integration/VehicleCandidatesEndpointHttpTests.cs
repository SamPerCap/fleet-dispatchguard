using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>The database is shared by the collection, so assertions only look at the vehicles seeded here.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class VehicleCandidatesEndpointHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public VehicleCandidatesEndpointHttpTests(PostgreSqlFixture db)
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
    public async Task VehicleCandidates_ExcludesSmallBusyAndInWorkshop_RanksLastTripPositionThenBaseThenUnknown()
    {
        var start = new DateTime(2036, 5, 6, 8, 0, 0, DateTimeKind.Utc);
        int tripId, comingBack, at12Km, unknown, small, busy, workshop, previousTripId;
        await using (var write = _db.CreateContext())
        {
            var origin = Loc("Origen", 40.4168m);
            var destination = Loc("Destino", 40.6000m);
            var near = Loc("Cerca", 40.4168m + 0.108m); // ~12 km north
            var far = Loc("Lejos", 42.0000m);            // ~176 km north
            var driver = TripRepositoryTests.NewDriver();
            var vBack = TripRepositoryTests.NewVehicle();
            var v12 = TripRepositoryTests.NewVehicle();
            var vNone = TripRepositoryTests.NewVehicle();
            var vSmall = new Vehicle($"SM-{Guid.NewGuid():N}"[..12], "Mercedes", "Sprinter", 8);
            var vBusy = TripRepositoryTests.NewVehicle();
            var vShop = TripRepositoryTests.NewVehicle();
            write.AddRange(origin, destination, near, far, driver, vBack, v12, vNone, vSmall, vBusy, vShop);
            await write.SaveChangesAsync();

            vBack.SetHomeLocation(far);
            v12.SetHomeLocation(near);
            vSmall.SetHomeLocation(origin);
            vBusy.SetHomeLocation(origin);
            vShop.SetHomeLocation(origin);
            vShop.AddUnavailability(new VehicleUnavailability(vShop.Id, new TimeWindow(start.AddHours(-2), start.AddHours(2)), VehicleUnavailabilityType.Repair));
            var previous = new Trip(far, origin, new TimeWindow(start.AddHours(-5), start.AddHours(-2)), 30, ServiceType.Regular);
            previous.AssignResources(driver, vBack);
            var overlapping = new Trip(origin, destination, new TimeWindow(start.AddHours(1), start.AddHours(3)), 10, ServiceType.Regular);
            overlapping.AssignResources(TripRepositoryTests.NewDriver(), vBusy);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(4)), 20, ServiceType.Regular);
            write.Trips.AddRange(previous, overlapping, trip);
            await write.SaveChangesAsync();
            (tripId, comingBack, at12Km, unknown, small, busy, workshop, previousTripId) =
                (trip.Id, vBack.Id, v12.Id, vNone.Id, vSmall.Id, vBusy.Id, vShop.Id, previous.Id);
        }

        var response = await _client.GetAsync($"/api/trips/{tripId}/vehicle-candidates?maxResults=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("VehicleId").GetInt32()).ToList();
        Assert.DoesNotContain(small, ids);
        Assert.DoesNotContain(busy, ids);
        Assert.DoesNotContain(workshop, ids);
        Assert.True(ids.IndexOf(comingBack) >= 0);
        Assert.True(ids.IndexOf(comingBack) < ids.IndexOf(at12Km) && ids.IndexOf(at12Km) < ids.IndexOf(unknown));

        var first = items[ids.IndexOf(comingBack)];
        Assert.Equal("BaseAtOrigin", first.GetProperty("Proximity").GetString());
        Assert.Equal("LastTripDestination", first.GetProperty("PositionSource").GetString());
        Assert.Contains($"#{previousTripId}", first.GetProperty("Reason").GetString());
        var mid = items[ids.IndexOf(at12Km)];
        Assert.Equal("HomeBase", mid.GetProperty("PositionSource").GetString());
        Assert.InRange(mid.GetProperty("DistanceKm").GetDouble(), 11.5, 12.5);
        Assert.Equal("Unknown", items[ids.IndexOf(unknown)].GetProperty("PositionSource").GetString());
    }

    [Fact]
    public async Task VehicleCandidates_UnknownTrip_Returns404Problem()
    {
        var response = await _client.GetAsync("/api/trips/999999/vehicle-candidates");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task VehicleCandidates_MaxResultsZero_Returns400Problem()
    {
        await using var write = _db.CreateContext();
        var origin = Loc("Origen", 40.4168m);
        var destination = Loc("Destino", 40.6000m);
        var start = new DateTime(2036, 6, 6, 8, 0, 0, DateTimeKind.Utc);
        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(2)), 10, ServiceType.Regular);
        write.AddRange(origin, destination, trip);
        await write.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/trips/{trip.Id}/vehicle-candidates?maxResults=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Location Loc(string name, decimal lat) =>
        new(name, $"{name} 1", new GeoCoordinate(lat, -3.7038m), "Madrid", "Madrid");
}
