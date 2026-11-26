using System.Net;
using System.Text;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HardeningHttpTests(PostgreSqlFixture db)
{
    private WebApplicationFactory<Program> Factory(string? connectionString = null, params (string Key, string Value)[] settings) =>
        new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString ?? db.ConnectionString);
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });

    public static TheoryData<string, string, string?, int> BadRequests => new()
    {
        { "GET", "/api/trips", null, 400 },
        { "GET", "/api/trips?from=abc&to=xyz", null, 400 },
        { "GET", "/api/trips?from=2030-01-01&to=2030-03-01", null, 400 },
        { "GET", "/api/trips?from=2030-01-02&to=2030-01-01", null, 400 },
        { "GET", "/api/trips?from=2030-01-01&to=2030-01-02&pageSize=100000", null, 400 },
        { "GET", "/api/optimization/vehicle-chaining?from=x&to=y", null, 400 },
        { "GET", "/api/optimization/vehicle-chaining?from=2030-01-01&to=2030-06-01", null, 400 },
        { "GET", "/api/drivers/999999", null, 404 },
        { "GET", "/api/vehicles/999999", null, 404 },
        { "GET", "/api/locations/999999", null, 404 },
        { "GET", "/api/clients/999999", null, 404 },
        { "GET", "/api/trips/999999", null, 404 },
        { "GET", "/api/trips/999999/driver-candidates", null, 404 },
        { "GET", "/api/trips/999999/vehicle-candidates", null, 404 },
        { "GET", "/api/trips/999999/driver-candidates?maxResults=0", null, 404 },
        { "POST", "/api/trips/999999/allocate", "{\"DriverId\":1,\"VehicleId\":1}", 404 },
        { "POST", "/api/trips/1/allocate", null, 400 },
        { "POST", "/api/trips/1/allocate", "{}", 400 },
        { "POST", "/api/trips/1/allocate", "{\"DriverId\":1}", 400 },
        { "POST", "/api/trips/1/allocate?driverId=1&vehicleId=1", null, 400 },
        { "POST", "/api/trips/999999/cancel", null, 404 },
        { "POST", "/api/drivers", "{", 400 },
        { "POST", "/api/drivers", "{}", 400 },
        { "POST", "/api/drivers", "[]", 400 },
        { "POST", "/api/vehicles", "{}", 400 },
        { "POST", "/api/vehicles", "{\"LicensePlate\":\"X\",\"Brand\":\"B\",\"Model\":\"M\",\"SeatingCapacity\":-5}", 400 },
        { "POST", "/api/locations", "{}", 400 },
        { "POST", "/api/locations", "{\"Name\":\"n\",\"Address\":\"a\",\"Latitude\":999,\"Longitude\":0,\"City\":\"c\",\"Province\":\"p\"}", 400 },
        { "POST", "/api/clients", "{}", 400 },
        { "POST", "/api/trips", "{}", 404 },
        { "POST", "/api/trips", "{\"OriginLocationId\":999999,\"DestinationLocationId\":999998,\"Start\":\"2030-01-01T10:00:00Z\",\"End\":\"2030-01-01T09:00:00Z\",\"RequiredSeats\":10,\"ServiceType\":\"Regular\"}", 404 },
        { "POST", "/api/trips", "{\"OriginLocationId\":999999,\"DestinationLocationId\":999998,\"Start\":\"2030-01-01T08:00:00Z\",\"End\":\"2030-01-01T09:00:00Z\",\"RequiredSeats\":10,\"ServiceType\":\"Nope\"}", 400 },
        { "POST", "/api/drivers/999999/unavailabilities", "{\"Start\":\"2030-01-01T08:00:00Z\",\"End\":\"2030-01-01T09:00:00Z\",\"Type\":\"Vacation\"}", 404 },
        { "PUT", "/api/drivers/999999/last-weekly-rest", "{}", 404 },
        { "PUT", "/api/drivers/1", "{", 400 },
        { "PUT", "/api/drivers/1", "{}", 400 },
        { "PUT", "/api/vehicles/1", "[]", 400 },
        { "PUT", "/api/vehicles/1", "{\"LicensePlate\":\"X\"}", 400 },
        { "PUT", "/api/drivers/999999", "{\"IdentificationNumber\":\"X\",\"FirstName\":\"A\",\"LastName\":\"B\",\"IsExternal\":false,\"AllowsAutoAssignment\":true,\"HomeLocationId\":null,\"BaseId\":1,\"DetailsVersion\":\"" + new string('0', 64) + "\"}", 404 },
        { "DELETE", "/api/drivers/999999", null, 404 },
        { "PUT", "/api/clients/999999", "{}", 400 },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task BadInput_ReturnsProblemDetails_NeverServerError(string method, string url, string? body, int expected)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(expected == (int)response.StatusCode, $"{method} {url} → {(int)response.StatusCode}: {content}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OutsideDevelopment_NoSwagger_AndSecurityHeaders()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);

        using var response = await client.GetAsync("/api/drivers");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Cors_OnlyConfiguredOrigins()
    {
        await using var factory = Factory(null, ("Cors:AllowedOrigins:0", "https://demo.example"));
        using var client = factory.CreateClient();

        foreach (var (origin, allowed) in new[] { ("https://demo.example", true), ("https://evil.example", false) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/drivers");
            request.Headers.Add("Origin", origin);
            using var response = await client.SendAsync(request);
            Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    [Fact]
    public async Task RateLimit_ExceedingPermitLimit_Returns429()
    {
        await using var factory = Factory(null, ("RateLimiting:Ip:Total", "3"))
            .WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient<IStartupFilter, FakeRemoteIp>()));
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/drivers")).StatusCode);
        using var limited = await client.GetAsync("/api/drivers");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DemoReset_RequiresConfiguredKey_AndWipesBusinessData()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "DispatchGuardReset" }.ConnectionString;
        await using (var ctx = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(connectionString).Options))
            await ctx.Database.MigrateAsync();

        await using (var noKey = Factory(connectionString))
        using (var client = noKey.CreateClient())
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/admin/demo/reset", null)).StatusCode);

        await using var factory = Factory(connectionString, ("Demo:ResetKey", "s3cret-test-key"));
        using var http = factory.CreateClient();
        var now = new DateTime(2041, 1, 9, 9, 0, 0, DateTimeKind.Utc);
        await new DispatchGuard.DemoData.ApiSeeder(http).SeedAsync(now);

        using (var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/admin/demo/reset"))
        {
            wrong.Headers.Add("X-Demo-Reset-Key", "nope");
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);
        }

        using (var ok = new HttpRequestMessage(HttpMethod.Post, "/api/admin/demo/reset"))
        {
            ok.Headers.Add("X-Demo-Reset-Key", "s3cret-test-key");
            Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(ok)).StatusCode);
        }

        await using (var ctx = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(connectionString).Options))
        {
            Assert.Equal(0, await ctx.Trips.CountAsync());
            Assert.Equal(0, await ctx.Drivers.CountAsync());
            Assert.Equal(0, await ctx.Vehicles.CountAsync());
            Assert.Equal(0, await ctx.Locations.CountAsync());
        }

        // Reseeding after a reset restores the full demo.
        var again = await new DispatchGuard.DemoData.ApiSeeder(http).SeedAsync(now);
        Assert.Equal(DispatchGuard.DemoData.DemoPlan.Build(now).Trips.Count(t => t.Covered), again.TripsAllocated);
    }
}

/// <summary>TestServer has no remote IP; pretend a public client so the limiter applies.</summary>
internal sealed class FakeRemoteIp : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nxt) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
            return nxt();
        });
        next(app);
    };
}
