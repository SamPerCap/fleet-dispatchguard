using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// A service across Monday counts in each week what falls in it, in both clocks, with the history read from the database
/// (CE 561/2006 Art. 6, SPEC 0064).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Regulation", "Art. 6")]
public sealed class Article6IntegrationTests : IDisposable
{
    private static readonly DateTime Monday = new(2038, 2, 1, 0, 0, 0, DateTimeKind.Utc); // Madrid = UTC+1

    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Article6IntegrationTests(PostgreSqlFixture db)
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

    [Theory]
    [InlineData(22.0, HttpStatusCode.OK)]                    // UTC 54 + 2 = 56 h
    [InlineData(21.75, HttpStatusCode.UnprocessableEntity)]  // UTC 54 + 2.25 h; in Madrid only 55.25 h
    public async Task Allocate_SundayNightAcrossMonday_IsCountedInBothClocks(double sundayHour, HttpStatusCode expected)
    {
        var (driver, vehicle, trip) = await SeedAsync(sundayHour);

        var response = await _client.AllocateAsync(trip, driver, vehicle);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, body);
        if (expected == HttpStatusCode.OK) return;
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal(["Conducción semanal: 56,25 h en UTC (máximo 56 h)"], json.GetProperty("violations").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains(json.GetProperty("explanation").GetProperty("Business").EnumerateArray(),
            i => i.GetProperty("Detail").GetString()!.Contains("Dos cuentas, en hora de Madrid y en UTC: la cifra es la más estricta, UTC"));
    }

    /// <summary>54 h from Monday to Saturday (two 4.5 h blocks a day) and a pending 3 h service on Sunday night.</summary>
    private async Task<(int Driver, int Vehicle, int Trip)> SeedAsync(double sundayHour)
    {
        await using var write = _db.CreateContext();
        var driver = TripRepositoryTests.NewDriver();
        var vehicle = TripRepositoryTests.NewVehicle();
        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        write.AddRange(driver, vehicle, origin, destination);
        await write.SaveChangesAsync();

        Trip NewTrip(DateTime start, double hours) =>
            new(origin, destination, new TimeWindow(start, start.AddHours(hours)), requiredSeats: 10, ServiceType.Regular);

        for (var d = 0; d < 6; d++)
            foreach (var hour in new[] { 7.0, 12.5 })
            {
                var done = NewTrip(Monday.AddDays(d).AddHours(hour), 4.5);
                done.AssignResources(driver, vehicle);
                write.Trips.Add(done);
            }

        var trip = NewTrip(Monday.AddDays(6).AddHours(sundayHour), 3);
        write.Trips.Add(trip);
        await write.SaveChangesAsync();
        return (driver.Id, vehicle.Id, trip.Id);
    }
}
