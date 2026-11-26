using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DispatchGuard.API.XRay;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// The X-ray of an answer, through HTTP against the real database (SPEC 0097): what it shows, and one test for each way it could leak.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class XRayHttpTests : IDisposable
{
    private const string GuardHeader = "X-DispatchGuard-Request";

    /// <summary>Every field name the X-ray may have; anything else is a leak (R2).</summary>
    private static readonly HashSet<string> AllowedFields =
    [
        "Kind", "Status", "TotalMs", "Queries", "QueriesMs", "Evaluations", "Checks", "Rules", "Stages", "OtherMs", "Locks", "Writes",
        "Step", "Outcome", "Rule", "Name", "Article", "Passed", "Actual", "Limit", "Unit", "Failed", "Stage", "Times", "Ms",
        "Resource", "Count", "Record", "Saved"
    ];

    private readonly PostgreSqlFixture _db;
    private readonly ShiftedClock _clock = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public XRayHttpTests(PostgreSqlFixture db)
    {
        _db = db;
        _factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Session:Required", "true");
            builder.ConfigureTestServices(services => services.AddSingleton(provider =>
                new XRayTokens(provider.GetRequiredService<IDataProtectionProvider>(), _clock)));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Allocate_Accepted_TheSameSessionSeesTheChecksInOrderStagesLocksAndWhatWasSaved()
    {
        var seed = await SeedAsync(new DateTime(2043, 3, 6, 8, 0, 0, DateTimeKind.Utc));
        var session = await StartSessionAsync();

        var token = await AllocateAsync(session, seed, HttpStatusCode.OK);
        var xray = await ReadAsync(session, token, HttpStatusCode.OK);

        Assert.InRange(token.Length, 1, 8_192); // A header every proxy lets through.
        Assert.Equal("allocation", xray.GetProperty("Kind").GetString());
        Assert.Equal(200, xray.GetProperty("Status").GetInt32());
        Assert.Equal(1, xray.GetProperty("Evaluations").GetInt32());
        var checks = xray.GetProperty("Checks").EnumerateArray().ToList();
        Assert.Equal(["driver-available", "compliance", "vehicle", "driver-overlap", "vehicle-overlap"],
            checks.Select(c => c.GetProperty("Step").GetString()));
        Assert.All(checks, c => Assert.Equal("passed", c.GetProperty("Outcome").GetString()));
        var daily = checks[1].GetProperty("Rules").EnumerateArray()
            .Single(r => r.GetProperty("Article").GetString() == "Art. 6.1");
        Assert.Contains(daily.GetProperty("Limit").GetDouble(), new[] { DrivingLimits.MaxDailyDriving.TotalHours, DrivingLimits.MaxDailyDrivingExtended.TotalHours });
        Assert.Equal(4, daily.GetProperty("Actual").GetDouble());

        var stages = xray.GetProperty("Stages").EnumerateArray().Select(s => s.GetProperty("Stage").GetString()).ToList();
        Assert.Equal(["locks", "read", "driver-available", "compliance", "vehicle", "driver-overlap", "vehicle-overlap", "assign", "record", "commit"], stages);
        Assert.True(xray.GetProperty("Queries").GetInt32() > 0);
        Assert.Equal(
            [("trip", 1), ("driver", 1), ("vehicle", 1)],
            xray.GetProperty("Locks").EnumerateArray().Select(l => (l.GetProperty("Resource").GetString(), l.GetProperty("Count").GetInt32())));
        Assert.Equal(
            [("service", true), ("attempt", true)],
            xray.GetProperty("Writes").EnumerateArray().Select(w => (w.GetProperty("Record").GetString(), w.GetProperty("Saved").GetBoolean())));
    }

    [Fact]
    public async Task Allocate_RejectedByCompliance_SaysWhichRuleWithItsFigureAndThatOnlyTheAttemptWasSaved()
    {
        var seed = await SeedAsync(new DateTime(2043, 4, 20, 8, 0, 0, DateTimeKind.Utc), noWeeklyRestDays: 7);
        var session = await StartSessionAsync();

        var token = await AllocateAsync(session, seed, HttpStatusCode.UnprocessableEntity);
        var xray = await ReadAsync(session, token, HttpStatusCode.OK);

        Assert.Equal(422, xray.GetProperty("Status").GetInt32());
        var checks = xray.GetProperty("Checks").EnumerateArray().ToDictionary(c => c.GetProperty("Step").GetString()!);
        Assert.Equal("passed", checks["driver-available"].GetProperty("Outcome").GetString());
        Assert.Equal("rejected", checks["compliance"].GetProperty("Outcome").GetString());
        Assert.Equal("not-reached", checks["vehicle"].GetProperty("Outcome").GetString());
        Assert.Equal("not-reached", checks["vehicle-overlap"].GetProperty("Outcome").GetString());
        var weekly = checks["compliance"].GetProperty("Rules").EnumerateArray().First(r => r.GetProperty("Article").GetString() == "Art. 8.6" && !r.GetProperty("Passed").GetBoolean());
        Assert.False(weekly.GetProperty("Passed").GetBoolean());
        Assert.False(string.IsNullOrEmpty(weekly.GetProperty("Name").GetString()));
        Assert.Equal(
            [("attempt", true)],
            xray.GetProperty("Writes").EnumerateArray().Select(w => (w.GetProperty("Record").GetString(), w.GetProperty("Saved").GetBoolean())));
    }

    [Fact]
    public async Task Read_FromAnotherSession_Returns404()
    {
        var seed = await SeedAsync(new DateTime(2043, 5, 6, 8, 0, 0, DateTimeKind.Utc));
        var owner = await StartSessionAsync();
        var token = await AllocateAsync(owner, seed, HttpStatusCode.OK);

        var stranger = await StartSessionAsync();

        await ReadAsync(stranger, token, HttpStatusCode.NotFound);
        await ReadAsync(owner, token, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Read_WithoutSession_Returns401()
    {
        var seed = await SeedAsync(new DateTime(2043, 5, 13, 8, 0, 0, DateTimeKind.Utc));
        var token = await AllocateAsync(await StartSessionAsync(), seed, HttpStatusCode.OK);

        await ReadAsync(cookie: null, token, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Read_AfterItsLifetime_Returns404()
    {
        var seed = await SeedAsync(new DateTime(2043, 5, 20, 8, 0, 0, DateTimeKind.Utc));
        var session = await StartSessionAsync();
        var token = await AllocateAsync(session, seed, HttpStatusCode.OK);

        _clock.Shift = XRayTokens.Lifetime - TimeSpan.FromSeconds(5);
        await ReadAsync(session, token, HttpStatusCode.OK);
        _clock.Shift = XRayTokens.Lifetime + TimeSpan.FromSeconds(1);
        await ReadAsync(session, token, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Read_GuessedOrAlteredOrMissing_Returns404()
    {
        var seed = await SeedAsync(new DateTime(2043, 5, 27, 8, 0, 0, DateTimeKind.Utc));
        var session = await StartSessionAsync();
        var token = await AllocateAsync(session, seed, HttpStatusCode.OK);

        var guessed = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(WebEncoders.Base64UrlDecode(token).Length));
        var middle = token.Length / 2;
        var altered = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];

        await ReadAsync(session, guessed, HttpStatusCode.NotFound);
        await ReadAsync(session, altered, HttpStatusCode.NotFound);
        await ReadAsync(session, "1", HttpStatusCode.NotFound);
        await ReadAsync(session, "not base64 at all!", HttpStatusCode.NotFound);
        await ReadAsync(session, token: null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Read_ForcedOverAnotherDriver_HoldsOnlyAllowedFieldsAndNothingSensitive()
    {
        var start = new DateTime(2043, 6, 20, 8, 0, 0, DateTimeKind.Utc);
        var seed = await SeedAsync(start, noWeeklyRestDays: 7);
        await AssignToAnotherDriverAsync(seed.TripId);
        var session = await StartSessionAsync();
        const string by = "Ana Forzadora";
        const string reason = "Motivo secreto de la prueba de fugas";

        var token = await AllocateAsync(session, seed, HttpStatusCode.OK, by, reason);
        var text = JsonSerializer.Serialize(await ReadAsync(session, token, HttpStatusCode.OK));

        using var parsed = JsonDocument.Parse(text);
        Assert.Empty(FieldNames(parsed.RootElement).Except(AllowedFields));
        Assert.Contains("overridden", text, StringComparison.Ordinal);
        foreach (var secret in new[] { by, reason, "SELECT", "INSERT", "Host=", "Password", "127.0.0.1", "lof:", "Cookie", TestSessionKey.Value })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }

        // Two drivers were locked, the previous one too, but the X-ray only counts them (R2).
        Assert.Contains(parsed.RootElement.GetProperty("Locks").EnumerateArray(),
            l => l.GetProperty("Resource").GetString() == "driver" && l.GetProperty("Count").GetInt32() == 2);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_OfARecommendation_ShowsItsQueriesWithoutLocksOrSaves()
    {
        var start = new DateTime(2043, 7, 6, 8, 0, 0, DateTimeKind.Utc);
        await SeedAsync(start);
        var session = await StartSessionAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/recommendations")
        {
            Content = JsonContent.Create(new { From = start.AddHours(-1), To = start.AddHours(5) })
        };
        request.Headers.Add("Cookie", session);
        request.Headers.Add(GuardHeader, "1");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var xray = await ReadAsync(session, response.Headers.GetValues(XRayTokens.Header).Single(), HttpStatusCode.OK);

        Assert.Equal("recommendation", xray.GetProperty("Kind").GetString());
        // Here the proposal may filter every pair before evaluating it; with several evaluations there is no single order.
        if (xray.GetProperty("Evaluations").GetInt32() != 1) Assert.Empty(xray.GetProperty("Checks").EnumerateArray());
        Assert.True(xray.GetProperty("Queries").GetInt32() > 0);
        Assert.Empty(xray.GetProperty("Locks").EnumerateArray());
        Assert.Empty(xray.GetProperty("Writes").EnumerateArray());
    }

    [Theory]
    [InlineData("recommendation")]
    [InlineData("driver-options")]
    [InlineData("driver-candidates")]
    public async Task Answer_TheWebAsksInTheBackground_HasNoXRay(string path)
    {
        var seed = await SeedAsync(new DateTime(2043, 7, 13, 8, 0, 0, DateTimeKind.Utc));
        var session = await StartSessionAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/trips/{seed.TripId}/{path}");
        request.Headers.Add("Cookie", session);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(XRayTokens.Header));
    }

    [Fact]
    public async Task Answer_OfAnEndpointNotMarked_HasNoXRay()
    {
        var session = await StartSessionAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/drivers");
        request.Headers.Add("Cookie", session);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(XRayTokens.Header));
    }

    private static IEnumerable<string> FieldNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => FieldNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(FieldNames),
        _ => []
    };

    private async Task<string> StartSessionAsync()
    {
        using var response = await _client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private async Task<string> AllocateAsync(
        string cookie, (int DriverId, int VehicleId, int TripId) seed, HttpStatusCode expected, string? by = null, string? reason = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/trips/{seed.TripId}/allocate")
        {
            Content = JsonContent.Create(new
            {
                seed.DriverId, seed.VehicleId, AssignedBy = by, Force = reason is not null, Reason = reason
            })
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(GuardHeader, "1");

        using var response = await _client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        return response.Headers.GetValues(XRayTokens.Header).Single();
    }

    private async Task<JsonElement> ReadAsync(string? cookie, string? token, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/xray");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (token is not null) request.Headers.Add(XRayTokens.Header, token);

        using var response = await _client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK) return default;
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    /// <summary>Gives the trip to another driver first, so that reassigning it locks that driver too.</summary>
    private async Task AssignToAnotherDriverAsync(int tripId)
    {
        await using var write = _db.CreateContext();
        var other = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var home = TripRepositoryTests.NewLocation("Base");
        write.AddRange(other, vehicle, home);
        await write.SaveChangesAsync();
        vehicle.SetHomeLocation(home);
        write.Trips.Single(t => t.Id == tripId).AssignResources(other, vehicle);
        await write.SaveChangesAsync();
    }

    private async Task<(int DriverId, int VehicleId, int TripId)> SeedAsync(DateTime start, int noWeeklyRestDays = 0)
    {
        await using var write = _db.CreateContext();
        var driver = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        write.AddRange(driver, vehicle, origin, destination);
        await write.SaveChangesAsync();
        // A known position: the km saving counts an unknown one as 0 km, and these vehicles would win every other test's plan.
        vehicle.SetHomeLocation(origin);

        // Weekly rest two days old and nothing in the last 24 h, or 1 h trips every 13 h that never leave a weekly rest.
        var past = new List<DateTime>();
        if (noWeeklyRestDays == 0) past.AddRange([start.AddDays(-5), start.AddDays(-2)]);
        for (var at = start.AddDays(-noWeeklyRestDays); noWeeklyRestDays > 0 && at <= start.AddHours(-2); at = at.AddHours(13)) past.Add(at);

        foreach (var at in past)
        {
            var done = new Trip(origin, destination, new TimeWindow(at, at.AddHours(1)), 10, ServiceType.Regular);
            done.AssignResources(driver, vehicle);
            done.Complete();
            write.Trips.Add(done);
        }

        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(4)), 30, ServiceType.Regular);
        write.Trips.Add(trip);
        await write.SaveChangesAsync();
        return (driver.Id, vehicle.Id, trip.Id);
    }

    private sealed class ShiftedClock : TimeProvider
    {
        public TimeSpan Shift { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Shift;
    }
}
