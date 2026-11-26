using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>The API in English, in Spanish and without asking for any (SPEC 0095 R2, R5).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LanguageHttpTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public LanguageHttpTests(PostgreSqlFixture db)
    {
        _db = db;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
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

    [Theory]
    [InlineData(null, "en", "Not found", "Service 987654321 does not exist")]
    [InlineData("en", "en", "Not found", "Service 987654321 does not exist")]
    [InlineData("en-US,en;q=0.9,es;q=0.8", "en", "Not found", "Service 987654321 does not exist")]
    [InlineData("fr", "en", "Not found", "Service 987654321 does not exist")]
    [InlineData("not a language;;", "en", "Not found", "Service 987654321 does not exist")]
    [InlineData("es", "es", "No encontrado", "El servicio 987654321 no existe")]
    [InlineData("es-ES", "es", "No encontrado", "El servicio 987654321 no existe")]
    [InlineData("fr,es;q=0.7,en;q=0.5", "es", "No encontrado", "El servicio 987654321 no existe")]
    [InlineData("es;q=0,en;q=0.1", "en", "Not found", "Service 987654321 does not exist")]
    public async Task Problem_IsInTheLanguageAskedFor_EnglishByDefault(string? accepted, string language, string title, string detail)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/trips/987654321", accepted);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
        Assert.Contains("Accept-Language", response.Headers.Vary);
        var problem = await JsonAsync(response);
        Assert.Equal((title, detail), (problem.GetProperty("title").GetString(), problem.GetProperty("detail").GetString()));
    }

    [Theory]
    [InlineData("en", "/api/not-an-endpoint", "Not found")]
    [InlineData("es", "/api/not-an-endpoint", "No encontrado")]
    [InlineData("en", "/api/trips/1/driver-candidates?maxResults=abc", "Malformed request")]
    [InlineData("es", "/api/trips/1/driver-candidates?maxResults=abc", "Petición incorrecta")]
    public async Task ErrorOfTheFramework_IsInTheLanguageAskedFor(string accepted, string path, string title)
    {
        var problem = await JsonAsync(await SendAsync(HttpMethod.Get, path, accepted));

        Assert.Equal(title, problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("en", "Method not allowed")]
    [InlineData("es", "Método no permitido")]
    public async Task MethodNotAllowed_IsInTheLanguageAskedFor(string accepted, string title)
    {
        var response = await SendAsync(HttpMethod.Delete, "/api/rules", accepted);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(title, (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("en", "Invalid request", "The name is required")]
    [InlineData("es", "Petición no válida", "El nombre es obligatorio")]
    public async Task Annotations_AreInTheLanguageAskedFor(string accepted, string title, string detail)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/clients", accepted, new { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await JsonAsync(response);
        Assert.Equal((title, detail), (problem.GetProperty("title").GetString(), problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Rules_SameIdsAndFigures_InBothLanguages()
    {
        var english = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/rules", "en"));
        var spanish = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/rules", "es"));

        var inEnglish = RulesOf(english);
        var inSpanish = RulesOf(spanish);
        Assert.Equal(inEnglish.Select(r => r.GetProperty("Id").GetString()), inSpanish.Select(r => r.GetProperty("Id").GetString()));
        Assert.Equal("Daily driving time", inEnglish.Single(r => r.GetProperty("Id").GetString() == "daily").GetProperty("Name").GetString());
        Assert.Equal("Conducción diaria", inSpanish.Single(r => r.GetProperty("Id").GetString() == "daily").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Attempt_IsReadInTheLanguageOfTheReader_WhateverTheOneWhoMadeIt()
    {
        var (tripId, driverId, vehicleId) = await SeedInactiveDriverAsync(new DateTime(2037, 5, 6, 8, 0, 0, DateTimeKind.Utc));

        var rejected = await SendAsync(HttpMethod.Post, $"/api/trips/{tripId}/allocate", "en", new { DriverId = driverId, VehicleId = vehicleId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var problem = await JsonAsync(rejected);
        Assert.Equal("The driver is not available for this service", problem.GetProperty("title").GetString());
        Assert.Equal("Driver not active", Assert.Single(problem.GetProperty("violations").EnumerateArray()).GetString());

        var id = await LastAttemptOfAsync(tripId);
        Assert.Equal(("Driver available: Driver not active", "Allocation rejected before saving anything: “Driver available” fails."), await ReadAsync(id, "en"));
        Assert.Equal(("Conductor disponible: Conductor no activo", "Asignación rechazada antes de guardar nada: falla «Conductor disponible»."), await ReadAsync(id, "es"));
    }

    /// <summary>As the previous version stored them: only worded, in Spanish.</summary>
    [Fact]
    public async Task AttemptRecordedBeforeTheCatalogue_IsStillReadAsItWasWritten()
    {
        var (tripId, driverId, vehicleId) = await SeedInactiveDriverAsync(new DateTime(2037, 6, 6, 8, 0, 0, DateTimeKind.Utc));
        const string stored = """
            {"Summary":"Asignación rechazada.","Business":[{"Title":"Conductor disponible","Reference":null,"Outcome":"Failed","Detail":"Conductor no activo"}],
             "Flow":[{"Component":"DriverAllocationService","Role":"Orquestador","Outcome":"Failed","Result":"Corta"}],"Patterns":["Patrón"],"Adrs":["docs/adr/x.md"]}
            """;
        await using (var write = _db.CreateContext())
        {
            var baseId = (await write.Trips.SingleAsync(t => t.Id == tripId)).BaseId;
            write.Add(new AllocationAttempt(DateTime.UtcNow, "Ana", tripId, driverId, vehicleId, baseId, AllocationOutcome.RejectedDriver,
                "Conductor disponible: Conductor no activo", explanation: stored));
            await write.SaveChangesAsync();
        }

        var id = await LastAttemptOfAsync(tripId);

        Assert.Equal(("Conductor disponible: Conductor no activo", "Asignación rechazada."), await ReadAsync(id, "en"));
        Assert.Equal(("Conductor disponible: Conductor no activo", "Asignación rechazada."), await ReadAsync(id, "es"));
    }

    private async Task<(int TripId, int DriverId, int VehicleId)> SeedInactiveDriverAsync(DateTime start)
    {
        await using var write = _db.CreateContext();
        var origin = TripRepositoryTests.NewLocation("O");
        var destination = TripRepositoryTests.NewLocation("D");
        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(2)), 30, ServiceType.Regular);
        var unique = Guid.NewGuid().ToString("N")[..10];
        var driver = new Driver(unique, "Ana", "Gil");
        driver.Deactivate();
        var vehicle = new Vehicle(unique, "Iveco", "Evadys", 50);
        write.AddRange(origin, destination, trip, driver, vehicle);
        await write.SaveChangesAsync();
        return (trip.Id, driver.Id, vehicle.Id);
    }

    private async Task<int> LastAttemptOfAsync(int tripId)
    {
        await using var read = _db.CreateContext();
        return await read.Set<AllocationAttempt>().Where(a => a.TripId == tripId).MaxAsync(a => a.Id);
    }

    private async Task<(string? Detail, string? Summary)> ReadAsync(int attemptId, string language)
    {
        var body = await JsonAsync(await SendAsync(HttpMethod.Get, $"/api/engine/attempts/{attemptId}", language));
        return (body.GetProperty("Attempt").GetProperty("Detail").GetString(), body.GetProperty("Explanation").GetProperty("Summary").GetString());
    }

    private static List<JsonElement> RulesOf(JsonElement groups) =>
        groups.EnumerateArray().SelectMany(group => group.GetProperty("Rules").EnumerateArray()).ToList();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? accepted, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accepted is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", accepted);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
