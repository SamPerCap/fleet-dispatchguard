using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AnonymousSessionHttpTests(PostgreSqlFixture db)
{
    private const string GuardHeader = "X-DispatchGuard-Request";
    private const string PreviousGuardHeader = "X-LOF-Request";

    private WebApplicationFactory<Program> Factory(string environment = "Testing", string? required = "true", string? connectionString = null) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString ?? db.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            if (required is not null) builder.UseSetting("Session:Required", required);
        });

    private static HttpClient WithoutCookieJar(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage NewBase(string? cookie = null, bool guardHeader = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bases")
        {
            Content = JsonContent.Create(new { Name = $"Base {Guid.NewGuid():N}" })
        };
        if (guardHeader) request.Headers.Add(GuardHeader, "1");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static async Task<(string Cookie, string ShortId, string SetCookie)> StartAsync(HttpClient client, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Single() : string.Empty;
        return (setCookie.Split(';')[0], body.GetProperty("ShortId").GetString()!, setCookie);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Write_WithoutSession_IsUnauthorizedAndSaysWhy()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);

        using var response = await client.SendAsync(NewBase());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("sessionMissing").GetBoolean());
    }

    /// <summary>The second one comes from a page opened before the rename (SPEC 0094 R3).</summary>
    [Theory]
    [InlineData(GuardHeader)]
    [InlineData(PreviousGuardHeader)]
    public async Task Write_WithAnyNameOfTheHeader_IsAccepted(string header)
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, _, _) = await StartAsync(client);
        using var request = NewBase(cookie, guardHeader: false);
        request.Headers.Add(header, "1");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The variable of the host before the rename (SPEC 0094 R3).</summary>
    [Fact]
    public async Task ConnectionString_OnlyWithItsPreviousName_IsUsed()
    {
        await using var factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:BetterBusDb", db.ConnectionString);
        });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/bases")).StatusCode);
    }

    [Fact]
    public async Task Write_WithSessionButWithoutHeader_IsForbidden()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, _, _) = await StartAsync(client);

        using var response = await client.SendAsync(NewBase(cookie, guardHeader: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ProblemAsync(response);
    }

    [Fact]
    public async Task Write_WithSessionAndHeader_IsAccepted()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, _, _) = await StartAsync(client);

        using var response = await client.SendAsync(NewBase(cookie));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("PUT", "/api/drivers/999999")]
    [InlineData("DELETE", "/api/drivers/999999")]
    [InlineData("POST", "/api/trips/999999/allocate")]
    [InlineData("POST", "/api/recommendations")]
    [InlineData("PATCH", "/api/drivers/999999")]
    public async Task AnyWrite_WithoutSession_NeverReachesTheOperation(string method, string url)
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Add(GuardHeader, "1");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_WithoutSession_IsUnauthorizedAndGetsNoCookie()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);

        using var response = await client.GetAsync("/api/bases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Read_WithSession_NeedsNoHeader()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, _, _) = await StartAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/bases");
        request.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DemoReset_NeedsItsKeyAndNoSession()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);

        // Without a configured key the endpoint does not exist (ADR 0014); 401 or 403 would mean that the guard answered.
        using var response = await client.PostAsync("/api/admin/demo/reset", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Session_SecondCall_KeepsTheSameId()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, first, _) = await StartAsync(client);

        var (_, second, _) = await StartAsync(client, cookie);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Session_ForgedCookie_GetsAnotherOneAndCannotWrite()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, original, _) = await StartAsync(client);
        var middle = cookie.Length / 2;
        var forged = cookie[..middle] + (cookie[middle] == 'A' ? 'B' : 'A') + cookie[(middle + 1)..];

        using var write = await client.SendAsync(NewBase(forged));
        var (renewed, replaced, _) = await StartAsync(client, forged);

        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
        Assert.NotEqual(original, replaced);
        Assert.NotEmpty(renewed);
    }

    [Fact]
    public async Task Session_AnotherInstanceOfTheApi_RecognisesIt()
    {
        await using var first = Factory();
        await using var second = Factory();
        using var here = WithoutCookieJar(first);
        using var there = WithoutCookieJar(second);
        var (cookie, shortId, _) = await StartAsync(here);

        var (_, recognised, _) = await StartAsync(there, cookie);
        using var write = await there.SendAsync(NewBase(cookie));

        Assert.Equal(shortId, recognised);
        Assert.True(write.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Session_InProduction_CookieIsSecureStrictAndHttpOnly()
    {
        await using var factory = Factory("Production", required: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false, BaseAddress = new Uri("https://localhost")
        });

        var (_, _, setCookie) = await StartAsync(client);

        Assert.StartsWith("__Host-", setCookie, StringComparison.Ordinal);
        Assert.Contains("; secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task Start_NotRequiredOutsideTesting_Fails(string environment)
    {
        await using var factory = Factory(environment, required: "false");

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Session:Required", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A database that is asleep must not hold the start of the API: its host would restart it.</summary>
    [Fact]
    public async Task Start_DatabaseNotAvailableYet_DoesNotWaitAndWorksAfterwards()
    {
        var later = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = $"Later{Guid.NewGuid():N}" }.ConnectionString;
        var clock = Stopwatch.StartNew();
        await using var factory = Factory(connectionString: later);
        using var client = WithoutCookieJar(factory);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"It took {clock.Elapsed}");
        await using (var context = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(later).Options))
        {
            await context.Database.MigrateAsync();
        }

        var (cookie, _, _) = await StartAsync(client);
        using var write = await client.SendAsync(NewBase(cookie));
        Assert.True(write.IsSuccessStatusCode, await write.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocate_KeepsTheSessionThatMadeTheAttempt()
    {
        await using var factory = Factory();
        using var client = WithoutCookieJar(factory);
        var (cookie, shortId, _) = await StartAsync(client);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add(GuardHeader, "1");
        var unique = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var origin = await CreateAsync(client, "/api/locations", Site($"O {unique}"));
        var destination = await CreateAsync(client, "/api/locations", Site($"D {unique}"));
        var driver = await CreateAsync(client, "/api/drivers", new { IdentificationNumber = unique, FirstName = "Ana", LastName = "Prueba" });
        var vehicle = await CreateAsync(client, "/api/vehicles", new { LicensePlate = unique, Brand = "Marca", Model = "Modelo", SeatingCapacity = 50 });
        var start = DateTime.UtcNow.Date.AddDays(40).AddHours(9);
        var trip = await CreateAsync(client, "/api/trips", new
        {
            OriginLocationId = origin, DestinationLocationId = destination, Start = start, End = start.AddHours(1), RequiredSeats = 10, ServiceType = 1
        });

        using var allocated = await client.AllocateAsync(trip, driver, vehicle, "Ana");

        Assert.True(allocated.IsSuccessStatusCode, await allocated.Content.ReadAsStringAsync());
        await using var context = Context();
        var attempt = await context.AllocationAttempts.SingleAsync(a => a.TripId == trip);
        Assert.Equal(32, attempt.SessionId!.Length);
        Assert.StartsWith(shortId, attempt.SessionId, StringComparison.Ordinal);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/engine/attempts/{attempt.Id}");
        Assert.Equal(shortId, detail.GetProperty("Attempt").GetProperty("Session").GetString());
    }

    private DispatchGuardDbContext Context() =>
        new(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(db.ConnectionString).Options);

    private static object Site(string name) =>
        new { Name = name, Address = "Calle", Latitude = 40.4m, Longitude = -3.7m, City = "Madrid", Province = "Madrid" };

    private static async Task<int> CreateAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetInt32();
    }
}
