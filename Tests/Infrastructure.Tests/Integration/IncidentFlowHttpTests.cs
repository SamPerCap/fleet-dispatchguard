using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// A last-minute sick leave exactly as the dispatcher web handles it: leave for the assigned driver → candidates without them →
/// reassign keeping the trip's vehicle (SPEC 0037).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IncidentFlowHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IncidentFlowHttpTests(PostgreSqlFixture db)
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
    public async Task SickLeave_ThenReplaceWithCandidate_KeepsVehicleAndAssignsNewDriver()
    {
        var start = new DateTime(2036, 6, 3, 8, 0, 0, DateTimeKind.Utc);
        int tripId, sickId, replacementId, vehicleId;
        await using (var write = _db.CreateContext())
        {
            var origin = new Location("Origen", "Origen 1", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");
            var destination = new Location("Destino", "Destino 1", new GeoCoordinate(40.6000m, -3.7038m), "Madrid", "Madrid");
            var vehicle = TripRepositoryTests.NewVehicle();
            var sick = TripRepositoryTests.NewDriver();
            var replacement = TripRepositoryTests.NewDriver();
            write.AddRange(origin, destination, vehicle, sick, replacement);
            await write.SaveChangesAsync();

            replacement.SetHomeLocation(origin);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(3)), 10, ServiceType.Regular);
            trip.AssignResources(sick, vehicle);
            write.Trips.Add(trip);
            await write.SaveChangesAsync();
            (tripId, sickId, replacementId, vehicleId) = (trip.Id, sick.Id, replacement.Id, vehicle.Id);
        }

        var leave = await _client.PostAsJsonAsync($"/api/drivers/{sickId}/unavailabilities",
            new { Start = start.AddHours(-1), End = start.AddHours(12), Type = "SickLeave", Notes = "Baja" });
        Assert.True(leave.IsSuccessStatusCode, await leave.Content.ReadAsStringAsync());

        var candidates = JsonDocument.Parse(await _client.GetStringAsync($"/api/trips/{tripId}/driver-candidates?maxResults=1000"))
            .RootElement.EnumerateArray().Select(c => c.GetProperty("DriverId").GetInt32()).ToList();
        Assert.DoesNotContain(sickId, candidates);
        Assert.Contains(replacementId, candidates);

        var allocate = await _client.AllocateAsync(tripId, replacementId, vehicleId);
        Assert.Equal(HttpStatusCode.OK, allocate.StatusCode);

        var trip2 = JsonDocument.Parse(await _client.GetStringAsync($"/api/trips/{tripId}")).RootElement;
        Assert.Equal(replacementId, trip2.GetProperty("AssignedDriverId").GetInt32());
        Assert.Equal(vehicleId, trip2.GetProperty("AssignedVehicleId").GetInt32());
    }
}
