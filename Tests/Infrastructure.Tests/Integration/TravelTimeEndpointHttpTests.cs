using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TravelTimeEndpointHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TravelTimeEndpointHttpTests(PostgreSqlFixture db)
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

    private async Task<JsonElement> TravelTime(int originId, int destinationId)
    {
        var response = await _client.GetAsync($"/api/locations/travel-time?originId={originId}&destinationId={destinationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task TravelTime_EstimatedThenHistorical_AfterCreatingTrips_CancelledAndReverseIgnored()
    {
        var day = new DateTime(2037, 5, 4, 0, 0, 0, DateTimeKind.Utc);
        Location placeA, placeB;
        await using (var write = _db.CreateContext())
        {
            placeA = new Location("Travel A", "A 1", new GeoCoordinate(40.0000m, -3.7000m), "Madrid", "Madrid");
            placeB = new Location("Travel B", "B 1", new GeoCoordinate(40.6295m, -3.7000m), "Madrid", "Madrid"); // ≈70 km
            write.AddRange(placeA, placeB);
            await write.SaveChangesAsync();
            var cancelled = new Trip(placeA, placeB, new TimeWindow(day.AddHours(6), day.AddHours(16)), 10, ServiceType.Regular);
            cancelled.Cancel();
            var reverse = new Trip(placeB, placeA, new TimeWindow(day.AddHours(6), day.AddHours(6.25)), 10, ServiceType.Regular);
            write.Trips.AddRange(cancelled, reverse);
            await write.SaveChangesAsync();
        }

        var estimated = await TravelTime(placeA.Id, placeB.Id);
        Assert.Equal("Estimated", estimated.GetProperty("Source").GetString());
        Assert.Equal(0, estimated.GetProperty("Samples").GetInt32());
        Assert.InRange(estimated.GetProperty("Minutes").GetInt32(), 59, 61);

        foreach (var minutes in new[] { 50, 70 })
        {
            var start = day.AddHours(8);
            var created = await _client.PostAsJsonAsync("/api/trips", new
            {
                OriginLocationId = placeA.Id, DestinationLocationId = placeB.Id,
                Start = start, End = start.AddMinutes(minutes), RequiredSeats = 10, ServiceType = "Regular"
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // The cached estimate was invalidated by the saves (SPEC 0055 R5).
        var historical = await TravelTime(placeA.Id, placeB.Id);
        Assert.Equal("Historical", historical.GetProperty("Source").GetString());
        Assert.Equal(2, historical.GetProperty("Samples").GetInt32());
        Assert.Equal(60, historical.GetProperty("Minutes").GetInt32());

        var same = await TravelTime(placeA.Id, placeA.Id);
        Assert.Equal("None", same.GetProperty("Source").GetString());
        Assert.Equal(JsonValueKind.Null, same.GetProperty("Minutes").ValueKind);
    }

    [Fact]
    public async Task TravelTime_UnknownLocation_Returns404()
    {
        var response = await _client.GetAsync($"/api/locations/travel-time?originId={int.MaxValue}&destinationId={int.MaxValue - 1}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
