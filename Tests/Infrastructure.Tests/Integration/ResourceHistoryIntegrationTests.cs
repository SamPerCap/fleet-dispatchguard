using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ResourceHistoryIntegrationTests : IDisposable
{
    // 20 Mar 2038, 12:00 in Madrid (CET, UTC+1): a date no other test uses. Today = 19 Mar 23:00Z → 20 Mar 23:00Z.
    private static readonly DateTime At = new(2038, 3, 20, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2038, 3, 19, 23, 0, 0, DateTimeKind.Utc);
    private static readonly GeoCoordinate MadridPoint = new(40.4168m, -3.7038m), ToledoPoint = new(39.8628m, -4.0273m);

    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ResourceHistoryIntegrationTests(PostgreSqlFixture db)
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

    private sealed record Seed(int Driver, int Vehicle, int OtherDriver, int TodayTrip, int YesterdayLate, int YesterdayEarly);

    /// <summary>
    /// Three done services of the driver+vehicle (yesterday 5 h Madrid→Toledo and 4.5 h back; today 2 h Madrid→Madrid),
    /// plus what must not count: one in progress, one cancelled, one before the window and another driver's.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        await using var write = _db.CreateContext();
        var madrid = new Location("Madrid hist", "Plaza 1", MadridPoint, "Madrid", "Madrid");
        var toledo = new Location("Toledo hist", "Zocodover 1", ToledoPoint, "Toledo", "Toledo");
        var driver = TripRepositoryTests.NewDriver();
        var other = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var vehicle2 = TripRepositoryTests.NewVehicle();
        Trip NewTrip(Location from, Location to, DateTime start, double hours, Driver tripDriver, Vehicle tripVehicle)
        {
            var trip = new Trip(from, to, new TimeWindow(start, start.AddHours(hours)), 10, ServiceType.Discretionary, $"Hist {start:O}");
            trip.AssignResources(tripDriver, tripVehicle);
            return trip;
        }

        var early = NewTrip(madrid, toledo, Today.AddHours(-17), 5, driver, vehicle);    // yesterday 07:00–12:00
        var late = NewTrip(toledo, madrid, Today.AddHours(-11), 4.5, driver, vehicle);   // yesterday 13:00–17:30
        var today = NewTrip(madrid, madrid, Today.AddHours(8), 2, driver, vehicle);      // today 08:00–10:00
        var running = NewTrip(madrid, toledo, At.AddMinutes(-30), 1.5, driver, vehicle); // in progress
        var cancelled = NewTrip(madrid, toledo, Today.AddDays(-3).AddHours(8), 2, driver, vehicle);
        cancelled.Cancel();
        var old = NewTrip(madrid, toledo, Today.AddDays(-40).AddHours(8), 2, driver, vehicle);
        var others = NewTrip(madrid, toledo, Today.AddHours(8), 3, other, vehicle2);
        write.AddRange(madrid, toledo, driver, other, vehicle, vehicle2, early, late, today, running, cancelled, old, others);
        await write.SaveChangesAsync();

        write.AllocationAttempts.AddRange(
            new AllocationAttempt(At.AddDays(-2), "Lucía", early.Id, driver.Id, vehicle.Id, 1, AllocationOutcome.Assigned),
            new AllocationAttempt(At.AddDays(-1), "Pablo", late.Id, driver.Id, vehicle2.Id, 1, AllocationOutcome.RejectedCompliance, "motivo", ["daily"]),
            new AllocationAttempt(At.AddDays(-35), "Pablo", old.Id, driver.Id, vehicle.Id, 1, AllocationOutcome.Assigned));
        await write.SaveChangesAsync();
        return new Seed(driver.Id, vehicle.Id, other.Id, today.Id, late.Id, early.Id);
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static string AtQuery => $"at={Uri.EscapeDataString(At.ToString("O"))}";

    [Fact]
    public async Task DriverHistory_OnlyDoneServicesOfTheDriver_HoursByMadridDay_NewestFirst()
    {
        var seed = await SeedAsync();

        var history = await GetAsync($"/api/drivers/{seed.Driver}/history?{AtQuery}");

        Assert.Equal(28, history.GetProperty("DaysCount").GetInt32());
        var days = history.GetProperty("Days").EnumerateArray().ToList();
        Assert.Equal(28, days.Count);
        Assert.Equal(9.0, history.GetProperty("DailyLimitHours").GetDouble());
        Assert.Equal(10.0, history.GetProperty("ExtendedDailyLimitHours").GetDouble());
        Assert.Equal(DrivingLimits.MaxExtendedDaysPerWeek, history.GetProperty("MaxExtendedDaysPerWeek").GetInt32());
        Assert.Equal(2.0, days[^1].GetProperty("Hours").GetDouble());
        Assert.Equal(1, days[^1].GetProperty("Trips").GetInt32());
        Assert.Equal(9.5, days[^2].GetProperty("Hours").GetDouble());
        Assert.Equal(2, days[^2].GetProperty("Trips").GetInt32());
        Assert.Equal(0.0, days.Take(26).Sum(d => d.GetProperty("Hours").GetDouble()));

        var totals = history.GetProperty("Totals");
        Assert.Equal(11.5, totals.GetProperty("Hours").GetDouble());
        Assert.Equal(3, totals.GetProperty("Trips").GetInt32());
        Assert.Equal(1, totals.GetProperty("DaysOverDaily").GetInt32());
        Assert.Equal(0, totals.GetProperty("DaysOverExtended").GetInt32());
        Assert.Equal(1, totals.GetProperty("TripsWithoutKm").GetInt32());
        var leg = Math.Round(MadridPoint.DistanceTo(ToledoPoint), 1);
        Assert.Equal(Math.Round(2 * leg, 1), totals.GetProperty("Km").GetDouble(), 6);

        var items = history.GetProperty("Items").EnumerateArray().ToList();
        Assert.Equal(new[] { seed.TodayTrip, seed.YesterdayLate, seed.YesterdayEarly }, items.Select(i => i.GetProperty("TripId").GetInt32()));
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("Km").ValueKind); // same origin and destination
        Assert.Equal(leg, items[2].GetProperty("Km").GetDouble(), 6);
        Assert.Equal(5.0, items[2].GetProperty("Hours").GetDouble());
        Assert.Equal(3, history.GetProperty("TotalCount").GetInt32());
    }

    [Fact]
    public async Task VehicleHistory_HasItsServicesAndKm_AndPages()
    {
        var seed = await SeedAsync();

        var page1 = await GetAsync($"/api/vehicles/{seed.Vehicle}/history?{AtQuery}&pageSize=2");
        var page2 = await GetAsync($"/api/vehicles/{seed.Vehicle}/history?{AtQuery}&pageSize=2&page=2");

        Assert.Equal("Vehicle", page1.GetProperty("Resource").GetString());
        Assert.Equal(3, page1.GetProperty("TotalCount").GetInt32());
        Assert.Equal(2, page1.GetProperty("TotalPages").GetInt32());
        Assert.Equal(new[] { seed.TodayTrip, seed.YesterdayLate }, page1.GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("TripId").GetInt32()));
        Assert.Equal(new[] { seed.YesterdayEarly }, page2.GetProperty("Items").EnumerateArray().Select(i => i.GetProperty("TripId").GetInt32()));
        var days = page1.GetProperty("Days").EnumerateArray().ToList();
        var leg = Math.Round(MadridPoint.DistanceTo(ToledoPoint), 1);
        Assert.Equal(Math.Round(2 * leg, 1), days[^2].GetProperty("Km").GetDouble(), 6);
        Assert.Equal(0.0, days[^1].GetProperty("Km").GetDouble());
        Assert.Equal(seed.Driver, page1.GetProperty("Items")[0].GetProperty("DriverId").GetInt32());
    }

    [Fact]
    public async Task History_UnknownResource_404_BadPaging_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/drivers/987654321/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/vehicles/987654321/history")).StatusCode);
        var seed = await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/drivers/{seed.Driver}/history?pageSize=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/drivers/{seed.Driver}/history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/vehicles/{seed.Vehicle}/history?page=0")).StatusCode);
    }

    [Fact]
    public async Task Audit_28Days_OnlyTheResourceAttemptsInsideTheWindow()
    {
        var seed = await SeedAsync();
        var date = Uri.EscapeDataString(At.ToString("O"));

        var byDriver = await GetAsync($"/api/engine/attempts?driverId={seed.Driver}&days=28&date={date}");
        var byVehicle = await GetAsync($"/api/engine/attempts?vehicleId={seed.Vehicle}&days=28&date={date}");
        var blocked = await GetAsync($"/api/engine/attempts?driverId={seed.Driver}&days=28&date={date}&kind=Blocked");

        Assert.Equal(2, byDriver.GetProperty("TotalCount").GetInt32()); // the one 35 days ago is out
        Assert.Equal(1, byDriver.GetProperty("Counts").GetProperty("Accepted").GetInt32());
        Assert.Equal(1, byDriver.GetProperty("Counts").GetProperty("Blocked").GetInt32());
        Assert.Equal(1, byVehicle.GetProperty("TotalCount").GetInt32());
        Assert.Equal("RejectedCompliance", Assert.Single(blocked.GetProperty("Items").EnumerateArray()).GetProperty("Outcome").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/engine/attempts?driverId={seed.Driver}&days=27")).StatusCode);
    }
}
