using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MasterDataEndpointsHttpTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MasterDataEndpointsHttpTests(PostgreSqlFixture db)
    {
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
    public async Task Driver_CreateGetDeclareRestAndDelete_RoundTripsThroughDatabase()
    {
        var locationId = await CreateLocationAsync();
        var created = await _client.PostAsJsonAsync("/api/drivers", new
        {
            IdentificationNumber = Unique("D"),
            FirstName = "Ana",
            LastName = "López",
            HomeLocationId = locationId,
            DeclaredLastWeeklyRestEnd = new DateTime(2026, 1, 4, 6, 0, 0, DateTimeKind.Utc)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("Id").GetInt32();

        var rest = await _client.PutAsJsonAsync($"/api/drivers/{id}/last-weekly-rest",
            new { RestEnd = new DateTime(2026, 2, 1, 6, 0, 0, DateTimeKind.Utc) });
        Assert.Equal(HttpStatusCode.OK, rest.StatusCode);

        var body = await JsonAsync(await _client.GetAsync($"/api/drivers/{id}"));
        Assert.Equal(locationId, body.GetProperty("HomeLocationId").GetInt32());
        Assert.Equal(new DateTime(2026, 2, 1, 6, 0, 0), body.GetProperty("DeclaredLastWeeklyRestEnd").GetDateTime());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/drivers/{id}")).StatusCode);
        var list = await JsonAsync(await _client.GetAsync("/api/drivers"));
        Assert.DoesNotContain(list.EnumerateArray(), d => d.GetProperty("Id").GetInt32() == id);
    }

    [Fact]
    public async Task Driver_DeclaredRestInTheFuture_Returns400Problem()
    {
        var response = await _client.PostAsJsonAsync("/api/drivers", new
        {
            IdentificationNumber = Unique("D"), FirstName = "Ana", LastName = "López",
            DeclaredLastWeeklyRestEnd = DateTime.UtcNow.AddDays(1)
        });

        await AssertProblemAsync(response, 400);
    }

    [Fact]
    public async Task Driver_DuplicateIdentificationNumber_Returns400Problem()
    {
        var number = Unique("D");
        var first = await _client.PostAsJsonAsync("/api/drivers", new { IdentificationNumber = number, FirstName = "A", LastName = "B" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/drivers", new { IdentificationNumber = number.ToLowerInvariant(), FirstName = "C", LastName = "D" });

        await AssertProblemAsync(second, 400);
    }

    [Fact]
    public async Task Driver_Unavailability_AddedThenRemoved()
    {
        var id = await CreateDriverAsync();

        var added = await _client.PostAsJsonAsync($"/api/drivers/{id}/unavailabilities", new
        {
            Start = new DateTime(2035, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2035, 6, 3, 0, 0, 0, DateTimeKind.Utc),
            Type = "SickLeave",
            Notes = "Baja de última hora"
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var unavailabilities = (await JsonAsync(await _client.GetAsync($"/api/drivers/{id}"))).GetProperty("Unavailabilities");
        var unavailability = Assert.Single(unavailabilities.EnumerateArray());
        Assert.Equal("SickLeave", unavailability.GetProperty("Type").GetString());

        var removed = await _client.DeleteAsync($"/api/drivers/{id}/unavailabilities/{unavailability.GetProperty("Id").GetInt32()}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var after = (await JsonAsync(await _client.GetAsync($"/api/drivers/{id}"))).GetProperty("Unavailabilities");
        Assert.Empty(after.EnumerateArray());
    }

    [Fact]
    public async Task Unavailability_EmptyWindow_Returns400Problem()
    {
        var id = await CreateDriverAsync();
        var at = new DateTime(2035, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        var response = await _client.PostAsJsonAsync($"/api/drivers/{id}/unavailabilities",
            new { Start = at, End = at, Type = "Vacation" });

        await AssertProblemAsync(response, 400);
    }

    [Fact]
    public async Task Vehicle_CreateWithUnavailabilityAndDelete()
    {
        var created = await _client.PostAsJsonAsync("/api/vehicles", new
        {
            LicensePlate = Unique("V"), Brand = "Irizar", Model = "i6", SeatingCapacity = 55
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("Id").GetInt32();

        var added = await _client.PostAsJsonAsync($"/api/vehicles/{id}/unavailabilities", new
        {
            Start = new DateTime(2035, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2035, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            Type = "Repair",
            ServiceProvider = "Taller Norte"
        });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var body = await JsonAsync(await _client.GetAsync($"/api/vehicles/{id}"));
        Assert.Equal(55, body.GetProperty("SeatingCapacity").GetInt32());
        Assert.Single(body.GetProperty("Unavailabilities").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/vehicles/{id}")).StatusCode);
    }

    [Fact]
    public async Task Vehicle_ZeroSeats_Returns400Problem()
    {
        var response = await _client.PostAsJsonAsync("/api/vehicles", new
        {
            LicensePlate = Unique("V"), Brand = "Irizar", Model = "i6", SeatingCapacity = 0
        });

        await AssertProblemAsync(response, 400);
    }

    [Fact]
    public async Task Trip_CreatedByApi_IsListedInRange_AllocatedAndCancelled()
    {
        var origin = await CreateLocationAsync();
        var destination = await CreateLocationAsync();
        var start = new DateTime(2035, 8, 6, 8, 0, 0, DateTimeKind.Utc);

        var created = await _client.PostAsJsonAsync("/api/trips", new
        {
            OriginLocationId = origin, DestinationLocationId = destination,
            Start = start, End = start.AddHours(2), RequiredSeats = 40, ServiceType = "Discretionary"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var trip = await JsonAsync(created);
        var tripId = trip.GetProperty("Id").GetInt32();
        Assert.Equal("Pending", trip.GetProperty("Status").GetString());

        var page = await JsonAsync(await _client.GetAsync(
            $"/api/trips?from={start.AddHours(-1):O}&to={start.AddHours(3):O}"));
        Assert.Contains(page.GetProperty("Items").EnumerateArray(), t => t.GetProperty("Id").GetInt32() == tripId);

        // A trip entered through the API can be allocated (driver with a declared rest).
        var driverId = await CreateDriverAsync(declaredRestEnd: start.AddDays(-1));
        var vehicle = await _client.PostAsJsonAsync("/api/vehicles", new
        {
            LicensePlate = Unique("V"), Brand = "Setra", Model = "S 515", SeatingCapacity = 50
        });
        var vehicleId = (await JsonAsync(vehicle)).GetProperty("Id").GetInt32();
        var allocate = await _client.AllocateAsync(tripId, driverId, vehicleId);
        Assert.Contains(allocate.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }); // The history decides (SPEC 0040).

        var cancelled = await _client.PostAsync($"/api/trips/{tripId}/cancel", null);
        Assert.Equal("Cancelled", (await JsonAsync(cancelled)).GetProperty("Status").GetString());
    }

    [Fact]
    public async Task Trip_UnknownOrigin_Returns404Problem()
    {
        var destination = await CreateLocationAsync();
        var start = new DateTime(2035, 8, 7, 8, 0, 0, DateTimeKind.Utc);

        var response = await _client.PostAsJsonAsync("/api/trips", new
        {
            OriginLocationId = int.MaxValue, DestinationLocationId = destination,
            Start = start, End = start.AddHours(1), RequiredSeats = 10, ServiceType = "Regular"
        });

        await AssertProblemAsync(response, 404);
    }

    [Fact]
    public async Task Trips_InvertedRange_Returns400Problem()
    {
        await AssertProblemAsync(await _client.GetAsync("/api/trips?from=2035-01-02&to=2035-01-01"), 400);
    }

    [Theory]
    [InlineData("2026-11-23T10:00:00Z")]
    [InlineData("2026-11-23T10:00:00")]
    [InlineData("2026-11-23T11:00:00%2B01:00")]
    public async Task DriverHours_MeasuresAtTheSameUtcInstant_WhateverTheZoneFormat(string at)
    {
        var id = await CreateDriverAsync();

        var hours = await JsonAsync(await _client.GetAsync($"/api/drivers/{id}/hours?at={at}"));

        Assert.Equal("2026-11-23T10:00:00Z", hours.GetProperty("At").GetString());
    }

    [Theory]
    [InlineData("/api/drivers/2147483647")]
    [InlineData("/api/vehicles/2147483647")]
    [InlineData("/api/locations/2147483647")]
    [InlineData("/api/trips/2147483647")]
    [InlineData("/api/clients/2147483647")]
    public async Task Get_UnknownId_Returns404Problem(string url)
    {
        await AssertProblemAsync(await _client.GetAsync(url), 404);
    }

    [Fact]
    public async Task Clients_SingleApi_ExternalListsOnlyActiveExternals()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/clientsv2")).StatusCode);

        var created = await _client.PostAsJsonAsync("/api/clients", new { Name = Unique("Cliente"), IsExternal = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("Id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/clients/{id}")).StatusCode);

        var externals = await JsonAsync(await _client.GetAsync("/api/clients/external"));
        Assert.DoesNotContain(externals.EnumerateArray(), c => c.GetProperty("Id").GetInt32() == id);
        await AssertProblemAsync(await _client.PutAsJsonAsync("/api/clients/2147483647", new { Name = "X" }), 404);
    }

    private async Task<int> CreateLocationAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/locations", new
        {
            Name = Unique("Parada"), Address = "C/ Mayor 1", Latitude = 40.4168m, Longitude = -3.7038m,
            City = "Madrid", Province = "Madrid"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private async Task<int> CreateDriverAsync(DateTime? declaredRestEnd = null)
    {
        var response = await _client.PostAsJsonAsync("/api/drivers", new
        {
            IdentificationNumber = Unique("D"), FirstName = "Luis", LastName = "Gil",
            DeclaredLastWeeklyRestEnd = declaredRestEnd is { } restEnd && restEnd <= DateTime.UtcNow ? restEnd : (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonAsync(response);
        Assert.Equal(status, body.GetProperty("status").GetInt32());
    }
}
