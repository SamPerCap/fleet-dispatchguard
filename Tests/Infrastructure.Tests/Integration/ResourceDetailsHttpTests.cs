using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DispatchGuard.Core.Entities;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Tests.Integration.ConcurrentAllocationTests;

namespace Infrastructure.Tests.Integration;

/// <summary>Changing the data of drivers and vehicles (SPECs 0079 and 0090). The races against an allocation are in <see cref="ConcurrentAllocationTests"/>.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ResourceDetailsHttpTests : IAsyncLifetime
{
    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _api;
    private readonly DateTime _day = new(2045, 3, 6, 0, 0, 0, DateTimeKind.Utc);

    public ResourceDetailsHttpTests(PostgreSqlFixture server)
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
    public async Task Driver_AllFields_RoundTripThroughTheDatabase()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var (url, newBase) = ($"/api/drivers/{fleet.Drivers[0]}", await BaseAsync("Valencia"));

        var changed = await JsonAsync(await _api.Client.PutAsJsonAsync(url, new
        {
            IdentificationNumber = " x1234567l ",
            FirstName = " Luisa ",
            LastName = "Gómez Ruiz",
            IsExternal = true,
            AllowsAutoAssignment = false,
            HomeLocationId = fleet.Origin,
            BaseId = newBase,
            DetailsVersion = await _api.Client.DetailsVersionAsync(url)
        }));

        var saved = await JsonAsync(await _api.Client.GetAsync(url));
        foreach (var driver in new[] { changed, saved })
        {
            Assert.Equal(("X1234567L", "Luisa", "Gómez Ruiz"), (Text(driver, "IdentificationNumber"), Text(driver, "FirstName"), Text(driver, "LastName")));
            Assert.Equal((true, false), (driver.GetProperty("IsExternal").GetBoolean(), driver.GetProperty("AllowsAutoAssignment").GetBoolean()));
            Assert.Equal((fleet.Origin, newBase), (driver.GetProperty("HomeLocationId").GetInt32(), driver.GetProperty("BaseId").GetInt32()));
        }

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("HomeLocationId", null))).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await _api.Client.GetAsync(url))).GetProperty("HomeLocationId").ValueKind);
    }

    [Fact]
    public async Task Vehicle_AllFields_RoundTripThroughTheDatabase()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var (url, newBase) = ($"/api/vehicles/{fleet.Vehicles[0]}", await BaseAsync("Henares"));

        var changed = await JsonAsync(await _api.Client.PutAsJsonAsync(url, new
        {
            LicensePlate = " 5678def ",
            Brand = "Setra",
            Model = " S 515 ",
            SeatingCapacity = 49,
            IsExternal = true,
            AllowsAutoAssignment = false,
            HomeLocationId = fleet.Destination,
            BaseId = newBase,
            DetailsVersion = await _api.Client.DetailsVersionAsync(url)
        }));

        var saved = await JsonAsync(await _api.Client.GetAsync(url));
        foreach (var vehicle in new[] { changed, saved })
        {
            Assert.Equal(("5678DEF", "Setra", "S 515", 49),
                (Text(vehicle, "LicensePlate"), Text(vehicle, "Brand"), Text(vehicle, "Model"), vehicle.GetProperty("SeatingCapacity").GetInt32()));
            Assert.Equal((true, false), (vehicle.GetProperty("IsExternal").GetBoolean(), vehicle.GetProperty("AllowsAutoAssignment").GetBoolean()));
            Assert.Equal((fleet.Destination, newBase), (vehicle.GetProperty("HomeLocationId").GetInt32(), vehicle.GetProperty("BaseId").GetInt32()));
        }

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("HomeLocationId", null))).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await _api.Client.GetAsync(url))).GetProperty("HomeLocationId").ValueKind);
    }

    /// <summary>They are not part of the form: the agenda and the declared weekly rest survive.</summary>
    [Fact]
    public async Task Change_KeepsWhatIsNotInTheRequest()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/drivers/{fleet.Drivers[0]}";
        await _api.Client.PostAsJsonAsync($"{url}/unavailabilities", new { Start = _day, End = _day.AddDays(1), Type = "Vacation" });
        await _api.Client.PutAsJsonAsync($"{url}/last-weekly-rest", new { RestEnd = new DateTime(2026, 1, 4, 6, 0, 0, DateTimeKind.Utc) });

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("FirstName", "Luisa"))).StatusCode);

        var saved = await JsonAsync(await _api.Client.GetAsync(url));
        Assert.Equal("Luisa", Text(saved, "FirstName"));
        Assert.Single(saved.GetProperty("Unavailabilities").EnumerateArray());
        Assert.Equal(new DateTime(2026, 1, 4, 6, 0, 0), saved.GetProperty("DeclaredLastWeeklyRestEnd").GetDateTime());
    }

    public static TheoryData<string, string, int> TextLimits => new()
    {
        { "drivers", "IdentificationNumber", Driver.MaxIdentificationNumberLength },
        { "drivers", "FirstName", Driver.MaxNameLength },
        { "drivers", "LastName", Driver.MaxNameLength },
        { "vehicles", "LicensePlate", Vehicle.MaxLicensePlateLength },
        { "vehicles", "Brand", Vehicle.MaxBrandOrModelLength },
        { "vehicles", "Model", Vehicle.MaxBrandOrModelLength }
    };

    /// <summary>The limits of the entity are the ones of the columns: one character more would be a 500.</summary>
    [Theory]
    [MemberData(nameof(TextLimits))]
    public async Task Texts_UpToTheColumn_WhenChangingAndWhenCreating(string kind, string field, int max)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var before = (await JsonAsync(await _api.Client.GetAsync(url))).ToString();

        foreach (var tooMuch in new object?[] { new string('x', max + 1), "", "   ", null })
        {
            await ProblemAsync(400, await PutAsync(url, (field, tooMuch)));
            await ProblemAsync(400, await _api.Client.PostAsJsonAsync($"/api/{kind}", await NewBodyAsync(url, (field, tooMuch))));
        }

        Assert.Equal(before, (await JsonAsync(await _api.Client.GetAsync(url))).ToString());
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, (field, new string('x', max)))).StatusCode);
    }

    [Fact]
    public async Task Seats_OneOrMore()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";

        Assert.Equal("Las plazas deben ser 1 o más", await ProblemAsync(400, await PutAsync(url, ("SeatingCapacity", 0))));
        await ProblemAsync(400, await PutAsync(url, ("SeatingCapacity", -1)));
        Assert.Equal(400, (int)(await PutAsync(url, ("SeatingCapacity", 3_000_000_000))).StatusCode);
        Assert.Equal(400, (int)(await PutAsync(url, ("SeatingCapacity", 10.5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("SeatingCapacity", 1))).StatusCode);
    }

    /// <summary>A field that is left out must not become «no location» or «not external».</summary>
    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task MissingField_Is400AndChangesNothing(string kind)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var before = (await JsonAsync(await _api.Client.GetAsync(url))).ToString();

        foreach (var field in (await BodyAsync(url)).Select(property => property.Key).ToList())
        {
            var body = await BodyAsync(url);
            body.Remove(field);
            var response = await _api.Client.PutAsJsonAsync(url, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"without {field}: {(int)response.StatusCode}");
        }

        Assert.Equal(before, (await JsonAsync(await _api.Client.GetAsync(url))).ToString());
    }

    [Theory]
    [InlineData("drivers", "IdentificationNumber")]
    [InlineData("vehicles", "LicensePlate")]
    public async Task Taken_Is409WithTheField_AlsoByADeactivatedOne(string kind, string field)
    {
        var fleet = await FleetAsync(_db, _day, 3, 3);
        var ids = kind == "drivers" ? fleet.Drivers : fleet.Vehicles;
        var url = $"/api/{kind}/{ids[0]}";
        var own = Text(await JsonAsync(await _api.Client.GetAsync(url)), field);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.Client.DeleteAsync($"/api/{kind}/{ids[2]}")).StatusCode);

        foreach (var other in ids[1..])
        {
            var taken = Text(await JsonAsync(await _api.Client.GetAsync($"/api/{kind}/{other}")), field)!;
            await ExpectTakenAsync(field, taken, await PutAsync(url, (field, taken.ToLowerInvariant())));
        }

        Assert.Equal(own, Text(await JsonAsync(await _api.Client.GetAsync(url)), field));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, (field, own!.ToLowerInvariant()))).StatusCode); // its own one is not taken
        Assert.Equal(own, Text(await JsonAsync(await _api.Client.GetAsync(url)), field));
    }

    /// <summary>Both pass the check before any of them is saved: the index stops the second one.</summary>
    [Theory]
    [InlineData("drivers", "IdentificationNumber")]
    [InlineData("vehicles", "LicensePlate")]
    public async Task TakenByTwoAtOnce_TheSecondIs409(string kind, string field)
    {
        var fleet = await FleetAsync(_db, _day, 2, 2);
        var ids = kind == "drivers" ? fleet.Drivers : fleet.Vehicles;
        using var other = new ApiInstance(_db.ConnectionString);
        var pause = _api.Saves.PauseNext();

        var first = PutAsync($"/api/{kind}/{ids[0]}", _api.Client, other.Client, (field, "REPETIDO"));
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var second = await PutAsync($"/api/{kind}/{ids[1]}", other.Client, other.Client, (field, "REPETIDO"));
        pause.Release();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await ExpectTakenAsync(field, "REPETIDO", await first);
        Assert.NotEqual("REPETIDO", Text(await JsonAsync(await _api.Client.GetAsync($"/api/{kind}/{ids[0]}")), field));
    }

    [Theory]
    [InlineData("drivers", "IdentificationNumber")]
    [InlineData("vehicles", "LicensePlate")]
    public async Task CreatedByTwoAtOnce_TheSecondIs409(string kind, string field)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var body = await NewBodyAsync(UrlOf(kind, fleet), (field, "REPETIDO"));
        using var other = new ApiInstance(_db.ConnectionString);
        var pause = _api.Saves.PauseNext();

        var first = _api.Client.PostAsJsonAsync($"/api/{kind}", body);
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var second = await other.Client.PostAsJsonAsync($"/api/{kind}", body);
        pause.Release();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await ExpectTakenAsync(field, "REPETIDO", await first);
        Assert.Equal(400, (int)(await _api.Client.PostAsJsonAsync($"/api/{kind}", body)).StatusCode); // the usual check, as before
    }

    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task UnknownResourceBaseOrLocation_Is404InSpanish(string kind)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var (inactiveBase, inactiveLocation) = (await BaseAsync("Cerrada"), fleet.Destination);
        await DeactivateAsync<OperatingBase>(inactiveBase);
        await DeactivateAsync<Location>(inactiveLocation);

        Assert.Contains("no existe", await ProblemAsync(404, await _api.Client.PutAsJsonAsync($"/api/{kind}/{int.MaxValue}", await BodyAsync(url))));
        foreach (var id in new[] { int.MaxValue, 0, -1, inactiveBase })
            Assert.Equal($"La base {id} no existe", await ProblemAsync(404, await PutAsync(url, ("BaseId", id))));
        foreach (var id in new[] { int.MaxValue, 0, -1, inactiveLocation })
            Assert.Equal($"La ubicación {id} no existe", await ProblemAsync(404, await PutAsync(url, ("HomeLocationId", id))));
    }

    /// <summary>A base or a location deactivated after being chosen must not block fixing a typo.</summary>
    [Theory]
    [InlineData("drivers", "FirstName")]
    [InlineData("vehicles", "Brand")]
    public async Task UnchangedInactiveBaseAndLocation_DoNotBlockTheRest(string kind, string field)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var closing = await BaseAsync("Se cierra");
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("BaseId", closing), ("HomeLocationId", fleet.Origin))).StatusCode);
        await DeactivateAsync<OperatingBase>(closing);
        await DeactivateAsync<Location>(fleet.Origin);

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, (field, "Corregido"))).StatusCode);

        var saved = await JsonAsync(await _api.Client.GetAsync(url));
        Assert.Equal(("Corregido", closing, fleet.Origin), (Text(saved, field), saved.GetProperty("BaseId").GetInt32(), saved.GetProperty("HomeLocationId").GetInt32()));
    }

    /// <summary>The service needs 10 seats: lowered to 10 it still fits, to 9 the next allocation is rejected and the assigned one stays.</summary>
    [Theory]
    [InlineData(10, true)]
    [InlineData(9, false)]
    public async Task LoweredSeats_DecideTheNextAllocationOnly(int seats, bool fits)
    {
        var fleet = await FleetAsync(_db, _day, 2, 1);
        var assigned = await TripAsync(_db, _day, fleet, 8, 2, driver: 0, vehicle: 0);
        var pending = await TripAsync(_db, _day, fleet, 12, 2);
        var url = $"/api/vehicles/{fleet.Vehicles[0]}";

        Assert.Equal([(assigned, 10)], await AffectedSeatsAsync(url));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("SeatingCapacity", seats))).StatusCode);
        Assert.Equal([(assigned, 10)], await AffectedSeatsAsync(url));

        await using (var read = _db.CreateContext())
        {
            var kept = await read.Trips.SingleAsync(t => t.Id == assigned);
            Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), (kept.AssignedDriverId, kept.AssignedVehicleId));
        }

        foreach (var (trip, driver) in new[] { (pending, 0), (assigned, 1) })
        {
            var response = await Allocate(_api.Client, trip, fleet.Drivers[driver], fleet.Vehicles[0]);
            Assert.Equal(fits ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode);
            if (!fits)
            {
                var problem = await JsonAsync(response);
                Assert.Equal(["Plazas insuficientes: tiene 9, el servicio necesita 10"], problem.GetProperty("violations").EnumerateArray().Select(v => v.GetString()));
                Assert.False(problem.TryGetProperty("forcible", out _));
            }
        }
    }

    /// <summary>Only what is proposed depends on the type and on the automatic proposal: assigning by hand is validated the same.</summary>
    [Fact]
    public async Task ExternalAndNotProposed_CanStillBeAssigned()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 8, 2);

        foreach (var url in new[] { $"/api/drivers/{fleet.Drivers[0]}", $"/api/vehicles/{fleet.Vehicles[0]}" })
            Assert.Equal(HttpStatusCode.OK, (await PutAsync(url, ("IsExternal", true), ("AllowsAutoAssignment", false))).StatusCode);

        var candidates = await JsonAsync(await _api.Client.GetAsync($"/api/trips/{trip}/driver-candidates?maxResults=50"));
        Assert.DoesNotContain(candidates.EnumerateArray(), c => c.GetProperty("DriverId").GetInt32() == fleet.Drivers[0]);
        Assert.Equal(HttpStatusCode.OK, (await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0])).StatusCode);
    }

    /// <summary>A and B open the same card: the one who saves second is told and nothing of theirs is written (SPEC 0090 R2).</summary>
    [Theory]
    [InlineData("drivers", "FirstName", "LastName")]
    [InlineData("vehicles", "Brand", "Model")]
    public async Task ChangedBySomebodyElse_Is409WithoutFieldAndSavesNothing(string kind, string field, string otherField)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var (seenByA, seenByB) = (await BodyAsync(url), await BodyAsync(url));
        seenByA[field] = "De A";
        seenByB[otherField] = "De B";

        var savedByA = await JsonAsync(await ExpectOkAsync(_api.Client.PutAsJsonAsync(url, seenByA)));
        Assert.NotEqual(Text(savedByA, "DetailsVersion"), seenByA["DetailsVersion"]!.GetValue<string>());
        await ExpectDetailsChangedAsync(await _api.Client.PutAsJsonAsync(url, seenByB));
        Assert.Equal(savedByA.ToString(), (await JsonAsync(await _api.Client.GetAsync(url))).ToString());

        seenByB["DetailsVersion"] = Text(savedByA, "DetailsVersion");
        seenByB[field] = "De A";
        await ExpectOkAsync(_api.Client.PutAsJsonAsync(url, seenByB));
    }

    [Theory]
    [InlineData("drivers", "FirstName")]
    [InlineData("vehicles", "Brand")]
    public async Task SavedWithoutChanges_IsAcceptedAndKeepsTheVersion(string kind, string field)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        await ExpectOkAsync(PutAsync(url, (field, "Ñandú «Ω» 日本")));
        var body = await BodyAsync(url);

        for (var time = 0; time < 2; time++)
        {
            var saved = await JsonAsync(await ExpectOkAsync(_api.Client.PutAsJsonAsync(url, body)));
            Assert.Equal(body["DetailsVersion"]!.GetValue<string>(), Text(saved, "DetailsVersion"));
        }
    }

    /// <summary>Holidays or a workshop added meanwhile are not those data.</summary>
    [Theory]
    [InlineData("drivers", "FirstName", "Vacation")]
    [InlineData("vehicles", "Brand", "Repair")]
    public async Task AgendaChangedMeanwhile_IsNotAConflict(string kind, string field, string type)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        var seen = await BodyAsync(url);
        seen[field] = "Corregido";
        var entry = new { Start = _day, End = _day.AddDays(1), Type = type };
        var entryId = (await JsonAsync(await ExpectOkAsync(_api.Client.PostAsJsonAsync($"{url}/unavailabilities", entry))))
            .GetProperty("Unavailabilities")[0].GetProperty("Id").GetInt32();
        await ExpectOkAsync(_api.Client.PutAsJsonAsync($"{url}/unavailabilities/{entryId}", new { Start = _day, End = _day.AddDays(2), Type = type }));
        if (kind == "drivers")
            await ExpectOkAsync(_api.Client.PutAsJsonAsync($"{url}/last-weekly-rest", new { RestEnd = new DateTime(2026, 1, 4, 6, 0, 0, DateTimeKind.Utc) }));

        var saved = await JsonAsync(await ExpectOkAsync(_api.Client.PutAsJsonAsync(url, seen)));

        Assert.Equal("Corregido", Text(saved, field));
        Assert.Single(saved.GetProperty("Unavailabilities").EnumerateArray());
    }

    /// <summary>Whoever sees old data is told that before anything else about what they sent.</summary>
    [Theory]
    [InlineData("drivers", "IdentificationNumber", "LastName")]
    [InlineData("vehicles", "LicensePlate", "Model")]
    public async Task OldData_AreToldBeforeATakenOrWrongValue(string kind, string unique, string field)
    {
        var fleet = await FleetAsync(_db, _day, 2, 2);
        var url = UrlOf(kind, fleet);
        var ids = kind == "drivers" ? fleet.Drivers : fleet.Vehicles;
        var taken = Text(await JsonAsync(await _api.Client.GetAsync($"/api/{kind}/{ids[1]}")), unique);
        var seen = await BodyAsync(url);
        await ExpectOkAsync(PutAsync(url, (field, "Otro")));

        foreach (var (name, value) in new (string, JsonNode?)[] { (unique, taken), (field, ""), ("BaseId", int.MaxValue) })
        {
            var body = seen.DeepClone().AsObject();
            body[name] = value;
            await ExpectDetailsChangedAsync(await _api.Client.PutAsJsonAsync(url, body));
        }
    }

    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task NotAVersion_Is400(string kind)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);

        foreach (var wrong in new object?[] { null, "", "abc", new string('z', 64), new string('A', 64) })
            await ProblemAsync(400, await PutAsync(url, ("DetailsVersion", wrong)));
        Assert.Equal(400, (int)(await PutAsync(url, ("DetailsVersion", 7))).StatusCode);
    }

    /// <summary>The list and the single resource give the same version, also from another instance of the API.</summary>
    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task Version_IsTheSameWhereverItIsRead(string kind)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var url = UrlOf(kind, fleet);
        using var other = new ApiInstance(_db.ConnectionString);
        var single = await JsonAsync(await _api.Client.GetAsync(url));

        var listed = (await JsonAsync(await other.Client.GetAsync($"/api/{kind}"))).EnumerateArray()
            .Single(r => r.GetProperty("Id").GetInt32() == single.GetProperty("Id").GetInt32());

        Assert.Equal(Text(single, "DetailsVersion"), Text(listed, "DetailsVersion"));
    }

    private static async Task<HttpResponseMessage> ExpectOkAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    private static async Task ExpectDetailsChangedAsync(HttpResponseMessage response)
    {
        Assert.NotEmpty(await ProblemAsync(409, response));
        var problem = await JsonAsync(response);
        Assert.Equal("Otra persona ha cambiado estos datos", Text(problem, "title"));
        Assert.False(problem.TryGetProperty("field", out _));
    }

    private async Task<(int TripId, int Seats)[]> AffectedSeatsAsync(string url) =>
        [.. (await JsonAsync(await _api.Client.GetAsync($"{url}/affected-trips?from={_day:s}Z&to={_day.AddDays(1):s}Z"))).EnumerateArray()
            .Select(t => (t.GetProperty("TripId").GetInt32(), t.GetProperty("RequiredSeats").GetInt32()))];

    private static string UrlOf(string kind, Fleet fleet) => $"/api/{kind}/{(kind == "drivers" ? fleet.Drivers : fleet.Vehicles)[0]}";

    private Task<HttpResponseMessage> PutAsync(string url, params (string Field, object? Value)[] changes) =>
        PutAsync(url, _api.Client, _api.Client, changes);

    /// <summary>What the resource has now, with those changes.</summary>
    private static async Task<HttpResponseMessage> PutAsync(
        string url, HttpClient client, HttpClient reader, params (string Field, object? Value)[] changes) =>
        await client.PutAsJsonAsync(url, await BodyAsync(url, reader, changes));

    private Task<JsonObject> BodyAsync(string url) => BodyAsync(url, _api.Client);

    private static async Task<JsonObject> BodyAsync(string url, HttpClient client, params (string Field, object? Value)[] changes)
    {
        var body = JsonNode.Parse(await (await client.GetAsync(url)).Content.ReadAsStringAsync())!.AsObject();
        foreach (var notInTheRequest in new[] { "Id", "IsActive", "Unavailabilities", "DeclaredLastWeeklyRestEnd" }) body.Remove(notInTheRequest);
        foreach (var (field, value) in changes) body[field] = JsonSerializer.SerializeToNode(value);
        return body;
    }

    /// <summary>To create another one like it.</summary>
    private async Task<JsonObject> NewBodyAsync(string url, params (string Field, object? Value)[] changes)
    {
        var unique = $"N-{Guid.NewGuid():N}"[..12];
        (string, object?)[] identity = [(url.Contains("drivers", StringComparison.Ordinal) ? "IdentificationNumber" : "LicensePlate", unique)];
        return await BodyAsync(url, _api.Client, [.. identity, .. changes]);
    }

    private async Task<int> BaseAsync(string name) =>
        (await JsonAsync(await _api.Client.PostAsJsonAsync("/api/bases", new { Name = $"{name} {Guid.NewGuid():N}" }))).GetProperty("Id").GetInt32();

    private async Task DeactivateAsync<TEntity>(int id) where TEntity : DispatchGuard.Core.Entities.Base.Entity
    {
        await using var write = _db.CreateContext();
        (await write.Set<TEntity>().SingleAsync(e => e.Id == id)).Deactivate();
        await write.SaveChangesAsync();
    }

    private static async Task ExpectTakenAsync(string field, string value, HttpResponseMessage response)
    {
        var detail = await ProblemAsync(409, response);
        var problem = await JsonAsync(response);
        Assert.Equal(field, problem.GetProperty("field").GetString());
        Assert.Contains(value, detail, StringComparison.Ordinal);
        Assert.Contains("ya es de otro", detail, StringComparison.Ordinal);
    }

    private static string? Text(JsonElement resource, string field) => resource.GetProperty(field).GetString();

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
