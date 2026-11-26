using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AllocateEndpointHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AllocateEndpointHttpTests(PostgreSqlFixture db)
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
    public async Task Allocate_ValidRequest_Returns200WithTripIdOfTheTrip()
    {
        var seed = await SeedAsync(new DateTime(2035, 3, 6, 8, 0, 0, DateTimeKind.Utc));

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.NotEqual(seed.DriverId, seed.TripId); // otherwise the assertion below proves nothing
        Assert.Equal(seed.TripId, body.GetProperty("TripId").GetInt32());
        Assert.Equal(seed.DriverId, body.GetProperty("DriverId").GetInt32());
        Assert.Equal(seed.VehicleId, body.GetProperty("VehicleId").GetInt32());

        // Every CE 561 rule with its figures, and the flow run by the code (SPEC 0042).
        var explanation = body.GetProperty("Explanation");
        var business = explanation.GetProperty("Business").EnumerateArray().ToList();
        Assert.Contains(business, i => i.GetProperty("Reference").GetString() == "Art. 6.1"
            && i.GetProperty("Detail").GetString()!.Contains(" h de "));
        Assert.Equal(7, business.Count(i => i.GetProperty("Reference").GetString()?.StartsWith("Art.", StringComparison.Ordinal) == true));
        var flow = explanation.GetProperty("Flow").EnumerateArray().Select(f => f.GetProperty("Component").GetString()).ToList();
        Assert.Equal("DriverAllocationService", flow[0]);
        Assert.Equal("Trip.AssignResources", flow[^1]);
        Assert.NotEmpty(explanation.GetProperty("Adrs").EnumerateArray());
    }

    [Fact]
    public async Task Allocate_DriverWithoutHistory_Returns200WithUnverifiableWeeklyRestWarning()
    {
        // No history cannot prove a missing weekly rest: a warning, not a 422 (SPEC 0040).
        var seed = await SeedAsync(new DateTime(2035, 4, 6, 8, 0, 0, DateTimeKind.Utc), withHistory: false);

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var validation = (await JsonAsync(response)).GetProperty("Validation");
        Assert.True(validation.GetProperty("IsCompliant").GetBoolean());
        Assert.Contains(validation.GetProperty("Warnings").EnumerateArray(),
            w => w.GetString()!.StartsWith("Historial insuficiente para verificar el descanso semanal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Allocate_DriverWithSevenDaysWithoutWeeklyRest_Returns422ProblemWithViolations()
    {
        var seed = await SeedAsync(new DateTime(2035, 4, 20, 8, 0, 0, DateTimeKind.Utc), withHistory: false, noWeeklyRestDays: 7);

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertProblemAsync(response, 422);
        var body = await JsonAsync(response);
        Assert.NotEmpty(body.GetProperty("violations").EnumerateArray());

        // The rejection explains the failing rule; later steps were not evaluated.
        var explanation = body.GetProperty("explanation");
        Assert.Contains(explanation.GetProperty("Business").EnumerateArray(),
            i => i.GetProperty("Reference").GetString() == "Art. 8.6" && i.GetProperty("Outcome").GetString() == "Failed");
        Assert.Contains(explanation.GetProperty("Flow").EnumerateArray(),
            f => f.GetProperty("Component").GetString() == "VehicleEligibilityService" && f.GetProperty("Outcome").GetString() == "NotEvaluated");
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 11)]
    public async Task Allocate_SplitShiftInEitherOrder_BothTripsReturn200(bool earlierFirst, int month)
    {
        // 07–11 and 15–19 the same day: the 4h gap is a break, the daily rest is overnight (SPEC 0041).
        var seed = await SeedAsync(new DateTime(2035, month, 6, 7, 0, 0, DateTimeKind.Utc));
        int laterTripId;
        await using (var write = _db.CreateContext())
        {
            var origin = TripRepositoryTests.NewLocation("O");
            var destination = TripRepositoryTests.NewLocation("D");
            var later = new Trip(origin, destination, new TimeWindow(
                new DateTime(2035, month, 6, 15, 0, 0, DateTimeKind.Utc),
                new DateTime(2035, month, 6, 19, 0, 0, DateTimeKind.Utc)), 30, ServiceType.Regular);
            write.AddRange(origin, destination, later);
            await write.SaveChangesAsync();
            laterTripId = later.Id;
        }

        var first = await PostAsync(earlierFirst ? seed.TripId : laterTripId, seed.DriverId, seed.VehicleId);
        var second = await PostAsync(earlierFirst ? laterTripId : seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(second.StatusCode == HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocate_VehicleUnavailable_Returns422ProblemWithViolations()
    {
        var seed = await SeedAsync(new DateTime(2035, 5, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: true);

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.NotEmpty(body.GetProperty("violations").EnumerateArray());
    }

    [Fact]
    public async Task Allocate_DriverOnOverlappingTrip_Returns409WithConflictingTripIds()
    {
        var start = new DateTime(2035, 6, 6, 8, 0, 0, DateTimeKind.Utc);
        var seed = await SeedAsync(start);
        int busyTripId;
        await using (var write = _db.CreateContext())
        {
            var driver = await write.Drivers.FindAsync(seed.DriverId);
            var otherVehicle = TripRepositoryTests.NewVehicle();
            var origin = TripRepositoryTests.NewLocation("O");
            var destination = TripRepositoryTests.NewLocation("D");
            var busy = new Trip(origin, destination, new TimeWindow(start.AddHours(1), start.AddHours(3)), 10, ServiceType.Regular);
            busy.AssignResources(driver!, otherVehicle);
            write.AddRange(otherVehicle, origin, destination, busy);
            await write.SaveChangesAsync();
            busyTripId = busy.Id;
        }

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemAsync(response, 409);
        var ids = (await JsonAsync(response)).GetProperty("conflictingTripIds").EnumerateArray().Select(e => e.GetInt32());
        Assert.Contains(busyTripId, ids);
        Assert.Contains(
            (await JsonAsync(response)).GetProperty("explanation").GetProperty("Business").EnumerateArray(),
            i => i.GetProperty("Detail").GetString()!.Contains($"#{busyTripId}"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Allocate_LockedOrCancelledTrip_Returns409(bool locked)
    {
        var seed = await SeedAsync(new DateTime(2035, locked ? 7 : 8, 6, 8, 0, 0, DateTimeKind.Utc));
        await using (var write = _db.CreateContext())
        {
            var trip = await write.Trips.FindAsync(seed.TripId);
            if (locked) trip!.Lock(); else trip!.Cancel();
            await write.SaveChangesAsync();
        }

        var response = await PostAsync(seed.TripId, seed.DriverId, seed.VehicleId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemAsync(response, 409);
    }

    [Theory]
    [InlineData("trip")]
    [InlineData("driver")]
    [InlineData("vehicle")]
    public async Task Allocate_MissingResource_Returns404Problem(string missing)
    {
        var seed = await SeedAsync(new DateTime(2035, 9, 6, 8, 0, 0, DateTimeKind.Utc));
        const int nope = int.MaxValue;

        var response = await PostAsync(
            missing == "trip" ? nope : seed.TripId,
            missing == "driver" ? nope : seed.DriverId,
            missing == "vehicle" ? nope : seed.VehicleId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemAsync(response, 404);
    }

    [Fact]
    public async Task Allocate_UnparseableBodyInDevelopment_Returns400Problem()
    {
        // Development makes minimal APIs throw BadHttpRequestException instead of answering 400.
        using var dev = _factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = dev.CreateClient();
        // Outside Testing nothing is written without a session (SPEC 0084).
        (await client.GetAsync("/api/session")).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("X-DispatchGuard-Request", "1");

        var response = await client.PostAsJsonAsync("/api/trips/1/allocate", new { DriverId = "abc", VehicleId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemAsync(response, 400);
    }

    [Fact]
    public async Task UnexpectedException_Returns500ProblemWithoutInternalMessage()
    {
        // A terminal middleware appended after the app's own pipeline throws on any
        // unmatched route: the global handler must turn it into a generic 500.
        using var throwing = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddTransient<IStartupFilter, ThrowingStartupFilter>()));
        using var client = throwing.CreateClient();

        var response = await client.GetAsync("/__boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemAsync(response, 500);
        Assert.DoesNotContain(ThrowingStartupFilter.Secret, await response.Content.ReadAsStringAsync());
    }

    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public const string Secret = "internal-detail-que-no-debe-salir";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(_ => throw new InvalidOperationException(Secret));
        };
    }

    [Theory]
    [InlineData("Lucía", null)]
    [InlineData("Lucía", "corto")] // Under 10 characters.
    [InlineData(null, "Sin otro conductor libre")]
    public async Task Allocate_ForceWithoutReasonOrName_Returns400AndAssignsNothing(string? by, string? reason)
    {
        var seed = await SeedAsync(new DateTime(2036, 7, 6, 8, 0, 0, DateTimeKind.Utc));

        var response = await _client.AllocateAsync(seed.TripId, seed.DriverId, seed.VehicleId, by, force: true, reason: reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var read = _db.CreateContext();
        Assert.Null(read.Trips.Single(t => t.Id == seed.TripId).AssignedDriverId);
    }

    private Task<HttpResponseMessage> PostAsync(int tripId, int driverId, int vehicleId) =>
        _client.AllocateAsync(tripId, driverId, vehicleId);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonAsync(response);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
    }

    private async Task<(int DriverId, int VehicleId, int TripId)> SeedAsync(
        DateTime start, bool withHistory = true, bool unavailableVehicle = false, int noWeeklyRestDays = 0)
    {
        await using var write = _db.CreateContext();
        var driver = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        write.AddRange(driver, vehicle, origin, destination);
        await write.SaveChangesAsync();

        // Same history as DriverAllocationIntegrationTests: weekly rest 2 days old, nothing in the last 24h.
        if (withHistory)
        {
            foreach (var daysBefore in new[] { 5, 2 })
            {
                var past = new Trip(origin, destination,
                    new TimeWindow(start.AddDays(-daysBefore), start.AddDays(-daysBefore).AddHours(2)), 10, ServiceType.Regular);
                past.AssignResources(driver, vehicle);
                past.Complete();
                write.Trips.Add(past);
            }
        }

        // 1h trips every 13h (12h gaps, never a weekly rest) covering N days before the trip.
        for (var t = start.AddDays(-noWeeklyRestDays); noWeeklyRestDays > 0 && t <= start.AddHours(-2); t = t.AddHours(13))
        {
            var past = new Trip(origin, destination, new TimeWindow(t, t.AddHours(1)), 10, ServiceType.Regular);
            past.AssignResources(driver, vehicle);
            past.Complete();
            write.Trips.Add(past);
        }

        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(4)), 30, ServiceType.Regular);
        write.Trips.Add(trip);
        if (unavailableVehicle)
        {
            vehicle.AddUnavailability(new VehicleUnavailability(
                vehicle.Id, new TimeWindow(start.AddDays(-1), start.AddDays(1)), VehicleUnavailabilityType.Repair));
        }
        await write.SaveChangesAsync();
        return (driver.Id, vehicle.Id, trip.Id);
    }
}
