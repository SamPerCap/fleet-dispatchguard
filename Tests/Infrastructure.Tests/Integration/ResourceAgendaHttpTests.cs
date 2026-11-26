using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Tests.Integration.ConcurrentAllocationTests;

namespace Infrastructure.Tests.Integration;

/// <summary>Adding, changing and deleting agenda entries, and what they catch (SPEC 0078). The races are in <see cref="ConcurrentAllocationTests"/>.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ResourceAgendaHttpTests : IAsyncLifetime
{
    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _api;
    private readonly DateTime _day = new(2044, 3, 8, 0, 0, 0, DateTimeKind.Utc);

    public ResourceAgendaHttpTests(PostgreSqlFixture server)
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

    [Fact]
    public async Task Vehicle_AddChangeDelete_RoundTripsThroughTheDatabase()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";
        var id = IdOf(await AddAsync(url, _day, _day.AddHours(2), "TechnicalInspection", "Cita 8:00", "ITV Getafe"));

        var changed = await _api.Client.PutAsJsonAsync($"{url}/unavailabilities/{id}", new
        {
            Start = "2044-03-09T09:00:00+01:00", // 08:00Z
            End = _day.AddDays(2),
            Type = "Repair",
            Notes = "  ",
            ServiceProvider = "Taller Norte"
        });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal((id, "Repair"), Summary(Single(await JsonAsync(changed))));
        var saved = Single(await JsonAsync(await _api.Client.GetAsync(url)));
        Assert.Equal((id, "Repair"), Summary(saved));
        Assert.Equal((_day.AddDays(1).AddHours(8), _day.AddDays(2)), (saved.GetProperty("Start").GetDateTime(), saved.GetProperty("End").GetDateTime()));
        Assert.Equal((null, "Taller Norte"), (saved.GetProperty("Notes").GetString(), saved.GetProperty("ServiceProvider").GetString()));

        Assert.Equal(HttpStatusCode.NoContent, (await _api.Client.DeleteAsync($"{url}/unavailabilities/{id}")).StatusCode);
        Assert.Empty(Entries(await JsonAsync(await _api.Client.GetAsync(url))));
    }

    [Fact]
    public async Task Driver_Change_KeepsTheOtherEntries()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/drivers/{fleet.Drivers[0]}";
        await AddAsync(url, _day, _day.AddDays(1), "Vacation");
        var training = Entries(await AddAsync(url, _day.AddDays(5), _day.AddDays(6), "Training"))[1].GetProperty("Id").GetInt32();

        var changed = await PutAsync(url, training, _day.AddDays(7), _day.AddDays(8), "PersonalLeave");

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var entries = Entries(await JsonAsync(await _api.Client.GetAsync(url)));
        Assert.Equal(["Vacation", "PersonalLeave"], entries.Select(e => e.GetProperty("Type").GetString()));
        Assert.Equal(_day, entries[0].GetProperty("Start").GetDateTime());
        Assert.Equal(_day.AddDays(8), entries[1].GetProperty("End").GetDateTime());
    }

    [Theory]
    [InlineData("drivers", "Vacation")]
    [InlineData("vehicles", "Cleaning")]
    public async Task Change_UnknownOrOfAnotherResource_Is404InSpanish(string kind, string type)
    {
        var fleet = await FleetAsync(_db, _day, 2, 2);
        var ids = kind == "drivers" ? fleet.Drivers : fleet.Vehicles;
        var id = IdOf(await AddAsync($"/api/{kind}/{ids[0]}", _day, _day.AddDays(1), type));

        foreach (var (resource, entry) in new[] { (ids[1], id), (ids[0], int.MaxValue), (int.MaxValue, id) })
        {
            var problem = await ProblemAsync(404, await PutAsync($"/api/{kind}/{resource}", entry, _day, _day.AddDays(2), type));
            Assert.Contains("no existe", problem, StringComparison.Ordinal);
        }

        Assert.Equal(_day.AddDays(1), Single(await JsonAsync(await _api.Client.GetAsync($"/api/{kind}/{ids[0]}"))).GetProperty("End").GetDateTime());
    }

    [Theory]
    [InlineData("drivers", "Vacation")]
    [InlineData("vehicles", "Cleaning")]
    public async Task AddOrChange_Invalid_Is400AndSavesNothing(string kind, string type)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/{kind}/{(kind == "drivers" ? fleet.Drivers : fleet.Vehicles)[0]}";
        var id = IdOf(await AddAsync(url, _day, _day.AddDays(1), type));
        var bodies = new object[]
        {
            new { Start = _day, End = _day, Type = type },
            new { Start = _day, End = _day.AddMinutes(-1), Type = type },
            new { End = _day, Type = type },
            new { Start = _day, End = _day.AddHours(1) },
            new { Start = _day, End = _day.AddHours(1), Type = 77 },
            new { Start = _day, End = _day.AddHours(1), Type = "Holiday" },
            new { Start = _day, End = _day.AddHours(1), Type = type, Notes = new string('x', 501) }
        };

        foreach (var body in bodies)
        {
            await ProblemAsync(400, await _api.Client.PostAsJsonAsync($"{url}/unavailabilities", body));
            await ProblemAsync(400, await _api.Client.PutAsJsonAsync($"{url}/unavailabilities/{id}", body));
        }

        var entry = Single(await JsonAsync(await _api.Client.GetAsync(url)));
        Assert.Equal((id, type), Summary(entry));
        var longest = await _api.Client.PutAsJsonAsync($"{url}/unavailabilities/{id}",
            new { Start = _day, End = _day.AddHours(1), Type = type, Notes = new string('x', 500) });
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
    }

    [Fact]
    public async Task Vehicle_WorkshopName_UpTo200Characters()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}/unavailabilities";

        await ProblemAsync(400, await _api.Client.PostAsJsonAsync(url,
            new { Start = _day, End = _day.AddHours(1), Type = "Repair", ServiceProvider = new string('x', 201) }));
        Assert.Equal(HttpStatusCode.OK, (await _api.Client.PostAsJsonAsync(url,
            new { Start = _day, End = _day.AddHours(1), Type = "Repair", ServiceProvider = new string('x', 200) })).StatusCode);
    }

    /// <summary>Late paperwork can be added, but what is over is neither changed nor deleted.</summary>
    [Theory]
    [InlineData("drivers", "SickLeave")]
    [InlineData("vehicles", "Repair")]
    public async Task Finished_CanBeAdded_ButNotChangedNorDeleted(string kind, string type)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/{kind}/{(kind == "drivers" ? fleet.Drivers : fleet.Vehicles)[0]}";
        var now = DateTime.UtcNow;
        var id = IdOf(await AddAsync(url, now.AddDays(-2), now.AddSeconds(-1), type));

        foreach (var response in new[] { await PutAsync(url, id, now, now.AddDays(1), type), await _api.Client.DeleteAsync($"{url}/unavailabilities/{id}") })
        {
            var detail = await ProblemAsync(409, response);
            Assert.StartsWith("Esta entrada terminó el", detail, StringComparison.Ordinal);
            Assert.EndsWith("queda como historial y no se puede cambiar ni borrar", detail, StringComparison.Ordinal);
        }

        Assert.Equal((id, type), Summary(Single(await JsonAsync(await _api.Client.GetAsync(url)))));
    }

    [Fact]
    public async Task Running_CanBeShortened_AndThenItIsHistory()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";
        var now = DateTime.UtcNow;
        var id = IdOf(await AddAsync(url, now.AddHours(-2), now.AddHours(2), "Repair"));

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, id, now.AddHours(-2), now.AddSeconds(-1), "Repair")).StatusCode);

        await ProblemAsync(409, await PutAsync(url, id, now.AddHours(-2), now.AddHours(2), "Repair"));
        await ProblemAsync(409, await _api.Client.DeleteAsync($"{url}/unavailabilities/{id}"));
    }

    [Fact]
    public async Task Itv_RejectsTheAllocation_UntilItIsDeleted()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";
        var id = IdOf(await AddAsync(url, _day.AddHours(11), _day.AddHours(13), "TechnicalInspection"));

        var rejected = await JsonAsync(await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]));
        Assert.Equal(422, rejected.GetProperty("status").GetInt32());
        Assert.Equal(["Vehículo no disponible (ITV) del 08/03/2044 12:00 al 08/03/2044 14:00"],
            rejected.GetProperty("violations").EnumerateArray().Select(v => v.GetString()));
        Assert.False(rejected.TryGetProperty("forcible", out _));

        Assert.Equal(HttpStatusCode.NoContent, (await _api.Client.DeleteAsync($"{url}/unavailabilities/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0])).StatusCode);
    }

    /// <summary>The service is 10:00–12:00: shortened to its departure it no longer catches it, one minute more and it does.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ChangedEntry_DecidesWithItsNewPeriod(int minutesAfterDeparture, bool accepted)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        var url = $"/api/drivers/{fleet.Drivers[0]}";
        var id = IdOf(await AddAsync(url, _day.AddHours(8), _day.AddHours(24), "Vacation"));

        await PutAsync(url, id, _day.AddHours(8), _day.AddHours(10).AddMinutes(minutesAfterDeparture), "Training");

        var response = await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]);
        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        if (!accepted)
        {
            Assert.Equal("Conductor no disponible (formación) del 08/03/2044 09:00 al 08/03/2044 11:01",
                (await JsonAsync(response)).GetProperty("detail").GetString());
        }
    }

    /// <summary>Saving over an assigned service is allowed and takes nothing away (ADR 0019); the service stays in the list.</summary>
    [Fact]
    public async Task AffectedTrips_SameBeforeAndAfterSaving_AndNothingIsUnassigned()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2, driver: 0, vehicle: 0);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";

        Assert.Equal([trip], await AffectedAsync(url, 8, 11));
        Assert.Equal(HttpStatusCode.OK, (await _api.Client.PostAsJsonAsync($"{url}/unavailabilities",
            new { Start = _day.AddHours(8), End = _day.AddHours(11), Type = "TechnicalInspection" })).StatusCode);
        Assert.Equal([trip], await AffectedAsync(url, 8, 11));

        var service = (await JsonAsync(await _api.Client.GetAsync(Affected(url, 8, 11))))[0];
        Assert.Equal(("Assigned", _day.AddHours(10), _day.AddHours(12), Booking.UnnamedCustomer),
            (service.GetProperty("Status").GetString(), service.GetProperty("Start").GetDateTime(), service.GetProperty("End").GetDateTime(),
                service.GetProperty("CustomerName").GetString()));
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]),
            (service.GetProperty("AssignedDriverId").GetInt32(), service.GetProperty("AssignedVehicleId").GetInt32()));
        await using var read = _db.CreateContext();
        Assert.Equal((await read.Trips.SingleAsync(t => t.Id == trip)).BookingId, service.GetProperty("BookingId").GetInt32());
    }

    /// <summary>The service is 10:00–12:00.</summary>
    [Theory]
    [InlineData(8, 10, false)]
    [InlineData(8, 10.02, true)]
    [InlineData(10.5, 11, true)]
    [InlineData(11.98, 14, true)]
    [InlineData(12, 14, false)]
    [InlineData(-24 * 400, 24 * 400, true)] // longer than the 31 days of other queries
    public async Task AffectedTrips_HalfOpenAndForTheWholePeriod(double from, double to, bool caught)
    {
        var fleet = await FleetAsync(_db, _day, 2, 2);
        var trip = await TripAsync(_db, _day, fleet, 10, 2, driver: 0, vehicle: 0);
        await TripAsync(_db, _day, fleet, 10, 2, driver: 1, vehicle: 1);
        await TripAsync(_db, _day, fleet, 10, 2);

        foreach (var url in new[] { $"/api/drivers/{fleet.Drivers[0]}", $"/api/vehicles/{fleet.Vehicles[0]}" })
            Assert.Equal(caught, (await AffectedAsync(url, from, to)).Contains(trip));
        Assert.DoesNotContain(trip, await AffectedAsync($"/api/drivers/{fleet.Drivers[1]}", from, to));
    }

    [Fact]
    public async Task AffectedTrips_CancelledOnesDoNotCount_AndTheyComeByDeparture()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var afternoon = await TripAsync(_db, _day, fleet, 15, 2, driver: 0, vehicle: 0);
        var morning = await TripAsync(_db, _day, fleet, 8, 2, driver: 0, vehicle: 0);
        var cancelled = await TripAsync(_db, _day, fleet, 11, 2, driver: 0, vehicle: 0);
        Assert.True((await _api.Client.PostAsync($"/api/trips/{cancelled}/cancel", null)).IsSuccessStatusCode);

        Assert.Equal([morning, afternoon], await AffectedAsync($"/api/drivers/{fleet.Drivers[0]}", 0, 24));
        Assert.Equal([morning, afternoon], await AffectedAsync($"/api/vehicles/{fleet.Vehicles[0]}", 0, 24));
    }

    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task AffectedTrips_UnknownResourceOrInvalidPeriod(string kind)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/{kind}/{(kind == "drivers" ? fleet.Drivers : fleet.Vehicles)[0]}";

        await ProblemAsync(404, await _api.Client.GetAsync(Affected($"/api/{kind}/{int.MaxValue}", 0, 1)));
        await ProblemAsync(400, await _api.Client.GetAsync(Affected(url, 1, 1)));
        await ProblemAsync(400, await _api.Client.GetAsync(Affected(url, 2, 1)));
        await ProblemAsync(400, await _api.Client.GetAsync($"{url}/affected-trips"));
    }

    private string Affected(string url, double from, double to) =>
        $"{url}/affected-trips?from={_day.AddHours(from):s}Z&to={_day.AddHours(to):s}Z";

    private async Task<int[]> AffectedAsync(string url, double from, double to) =>
        (await JsonAsync(await _api.Client.GetAsync(Affected(url, from, to)))).EnumerateArray().Select(t => t.GetProperty("TripId").GetInt32()).ToArray();

    private async Task<JsonElement> AddAsync(string url, DateTime start, DateTime end, string type, string? notes = null, string? provider = null)
    {
        var response = await _api.Client.PostAsJsonAsync($"{url}/unavailabilities",
            new { Start = start, End = end, Type = type, Notes = notes, ServiceProvider = provider });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private Task<HttpResponseMessage> PutAsync(string url, int id, DateTime start, DateTime end, string type) =>
        _api.Client.PutAsJsonAsync($"{url}/unavailabilities/{id}", new { Start = start, End = end, Type = type });

    private static JsonElement[] Entries(JsonElement resource) => [.. resource.GetProperty("Unavailabilities").EnumerateArray()];

    private static JsonElement Single(JsonElement resource) => Assert.Single(Entries(resource));

    private static int IdOf(JsonElement resource) => Single(resource).GetProperty("Id").GetInt32();

    private static (int, string?) Summary(JsonElement entry) => (entry.GetProperty("Id").GetInt32(), entry.GetProperty("Type").GetString());

    private static async Task<string> ProblemAsync(int status, HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"{(int)response.StatusCode}: {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(text).RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "";
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
