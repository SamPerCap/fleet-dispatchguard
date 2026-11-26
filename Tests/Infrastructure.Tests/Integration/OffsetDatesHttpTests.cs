using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// A request date with an explicit offset is the instant it says, whatever the server's time zone (SPEC 0067). The failures
/// without the conversion only show on a server that is not in UTC (a developer's machine; the CI runs in UTC).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OffsetDatesHttpTests : IAsyncLifetime
{
    private static readonly DateTime Day = new(2045, 3, 8, 0, 0, 0, DateTimeKind.Utc);
    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _api;

    public OffsetDatesHttpTests(PostgreSqlFixture server)
    {
        _db = new ConcurrencyDatabase(server);
        _api = new ApiInstance(_db.ConnectionString);
    }

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync()
    {
        _api.Dispose();
        return _db.DisposeAsync();
    }

    /// <summary>The same two hours, 10:00–12:00 UTC, written in every accepted way. Last one: the hour that Madrid repeats.</summary>
    public static TheoryData<string, string, DateTime> Windows => new()
    {
        { "2045-03-08T10:00:00Z", "2045-03-08T12:00:00Z", Day.AddHours(10) },
        { "2045-03-08T10:00:00", "2045-03-08T12:00:00", Day.AddHours(10) },
        { "2045-03-08T11:00:00+01:00", "2045-03-08T13:00:00+01:00", Day.AddHours(10) },
        { "2045-03-08T05:00:00-05:00", "2045-03-08T07:00:00-05:00", Day.AddHours(10) },
        { "2045-03-08T15:30:00+05:30", "2045-03-08T17:30:00+05:30", Day.AddHours(10) },
        { "2045-03-08T10:00:00+00:00", "2045-03-08T12:00:00+00:00", Day.AddHours(10) },
        { "2026-10-25T02:30:00+02:00", "2026-10-25T03:30:00+01:00", new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc) },
        { "2026-10-25T02:30:00+01:00", "2026-10-25T04:30:00+01:00", new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc) },
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task EveryInputDate_IsSavedAsTheInstantItSays(string start, string end, DateTime expected)
    {
        var fleet = await ConcurrentAllocationTests.FleetAsync(_db, Day, 1, 1);
        await PostAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities", new { Start = start, End = end, Type = "SickLeave" });
        await PostAsync($"/api/vehicles/{fleet.Vehicles[0]}/unavailabilities", new { Start = start, End = end, Type = "Repair" });
        var trip = Trip(fleet, start, end, "suelto");
        await PostAsync("/api/trips", trip);
        await PostAsync("/api/bookings", new { CustomerName = "Cliente", Description = "Reserva", Trips = new[] { Trip(fleet, start, end, "reserva") } });

        await using var read = _db.CreateContext();
        Assert.Equal(expected, (await read.DriverUnavailabilities.SingleAsync()).Period.Start);
        Assert.Equal(expected, (await read.VehicleUnavailabilities.SingleAsync()).Period.Start);
        Assert.Equal([expected, expected], await read.Trips.Where(t => t.Description != null).Select(t => t.ScheduledWindow.Start).ToListAsync());
    }

    [Theory]
    [InlineData("2026-01-05T06:00:00Z")]
    [InlineData("2026-01-05T06:00:00")]
    [InlineData("2026-01-05T07:00:00+01:00")]
    [InlineData("2026-01-05T01:00:00-05:00")]
    public async Task WeeklyRest_IsSavedAsTheInstantItSays(string restEnd)
    {
        var fleet = await ConcurrentAllocationTests.FleetAsync(_db, Day, 1, 1);
        var response = await _api.Client.PutAsJsonAsync($"/api/drivers/{fleet.Drivers[0]}/last-weekly-rest", new { RestEnd = restEnd });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        await using var read = _db.CreateContext();
        Assert.Equal(new DateTime(2026, 1, 5, 6, 0, 0), (await read.Drivers.SingleAsync(d => d.Id == fleet.Drivers[0])).DeclaredLastWeeklyRestEnd);
    }

    /// <summary>The service leaves at 10:00 UTC; a leave until 11:00+01:00 ends right then, one until 11:01+01:00 does not.</summary>
    [Theory]
    [InlineData("2045-03-08T11:00:00+01:00", HttpStatusCode.OK)]
    [InlineData("2045-03-08T11:01:00+01:00", HttpStatusCode.UnprocessableEntity)]
    public async Task LeaveWithOffset_DecidesTheAssignmentAtItsInstant(string end, HttpStatusCode expected)
    {
        var fleet = await ConcurrentAllocationTests.FleetAsync(_db, Day, 1, 1);
        var trip = await ConcurrentAllocationTests.TripAsync(_db, Day, fleet, 10, 2);
        await PostAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities", new { Start = "2045-03-08T07:00:00+01:00", End = end, Type = "SickLeave" });

        Assert.Equal(expected, (await ConcurrentAllocationTests.Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0])).StatusCode);
    }

    /// <summary>Query dates: the range 11:00–13:00+01:00 finds the 10:00–12:00 UTC service, and the following hours do not.</summary>
    [Theory]
    [InlineData("/api/trips")]
    [InlineData("/api/bookings")]
    public async Task QueryDatesWithOffset_AreTheInstantTheySay(string path)
    {
        var fleet = await ConcurrentAllocationTests.FleetAsync(_db, Day, 1, 1);
        await PostAsync("/api/trips", Trip(fleet, "2045-03-08T10:00:00Z", "2045-03-08T12:00:00Z", "suelto"));

        Assert.Equal(1, await CountAsync("2045-03-08T11:00:00+01:00", "2045-03-08T13:00:00+01:00"));
        Assert.Equal(0, await CountAsync("2045-03-08T13:00:00+01:00", "2045-03-08T15:00:00+01:00"));
        Assert.Equal(0, await CountAsync("2045-03-08T09:00:00+01:00", "2045-03-08T11:00:00+01:00"));
        Assert.Equal(1, await CountAsync("2045-03-08T10:00:00Z", "2045-03-08T12:00:00Z"));

        async Task<int> CountAsync(string from, string to)
        {
            var body = await (await _api.Client.GetAsync($"{path}?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}")).Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            return (json.ValueKind == JsonValueKind.Array ? json : json.GetProperty("Items")).GetArrayLength();
        }
    }

    /// <summary>What the API writes does not change: the stored UTC figures, with no zone.</summary>
    [Fact]
    public async Task Responses_KeepTheirFormat()
    {
        var fleet = await ConcurrentAllocationTests.FleetAsync(_db, Day, 1, 1);
        await PostAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities", new { Start = "2045-03-08T11:00:00+01:00", End = "2045-03-08T13:00:00+01:00", Type = "SickLeave" });

        var driver = JsonDocument.Parse(await _api.Client.GetStringAsync($"/api/drivers/{fleet.Drivers[0]}")).RootElement;
        Assert.Equal("2045-03-08T10:00:00", driver.GetProperty("Unavailabilities")[0].GetProperty("Start").GetString());
    }

    private static object Trip(ConcurrentAllocationTests.Fleet fleet, string start, string end, string description) => new
    {
        OriginLocationId = fleet.Origin, DestinationLocationId = fleet.Destination, Start = start, End = end, RequiredSeats = 1, ServiceType = "Regular", Description = description
    };

    private async Task PostAsync(string path, object body)
    {
        var response = await _api.Client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {await response.Content.ReadAsStringAsync()}");
    }
}
