using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using static Infrastructure.Tests.Integration.ConcurrentAllocationTests;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// An inactive or absent driver is never assigned, forced or not (SPEC 0066). The race with a leave being registered is in
/// <see cref="ConcurrentAllocationTests"/>.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DriverAvailabilityAllocationTests : IAsyncLifetime
{
    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _api;
    private readonly DateTime _day = new(2044, 3, 8, 0, 0, 0, DateTimeKind.Utc);

    public DriverAvailabilityAllocationTests(PostgreSqlFixture server)
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

    /// <summary>The service is 10:00–12:00.</summary>
    [Theory]
    [InlineData(8, 10, true)]
    [InlineData(12, 14, true)]
    [InlineData(8, 10.02, false)]
    [InlineData(11.98, 14, false)]
    [InlineData(10.5, 11, false)]
    [InlineData(-48, 72, false)]
    public async Task Allocate_AbsenceAgainstTheService_RejectsOnlyIfItOverlaps(double from, double to, bool accepted)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        await LeaveAsync(fleet.Drivers[0], from, to);

        var response = await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]);

        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(accepted ? fleet.Drivers[0] : null, (await ReadAsync(trip)).AssignedDriverId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocate_OnLeave_IsRejectedWithItsOwnReason_Audited_AndNeverForcible(bool force)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        await LeaveAsync(fleet.Drivers[0], 7, 23);

        var response = await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0], force);

        const string reason = "Conductor no disponible (baja médica) del 08/03/2044 08:00 al 09/03/2044 00:00";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("El conductor no está disponible para este servicio", body.GetProperty("title").GetString());
        Assert.Equal(reason, body.GetProperty("detail").GetString());
        Assert.Equal([reason], body.GetProperty("violations").EnumerateArray().Select(v => v.GetString()));
        Assert.False(body.TryGetProperty("forcible", out _));
        var item = body.GetProperty("explanation").GetProperty("Business")[0];
        Assert.Equal(("Conductor disponible", "Failed", reason),
            (item.GetProperty("Title").GetString(), item.GetProperty("Outcome").GetString(), item.GetProperty("Detail").GetString()));

        var saved = await ReadAsync(trip);
        Assert.Equal((null, null, TripStatus.Pending, false), (saved.AssignedDriverId, saved.AssignedVehicleId, saved.Status, saved.IsForced));
        await using var read = _db.CreateContext();
        var attempt = await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == trip);
        Assert.Equal((AllocationOutcome.RejectedDriver, true, "test", $"Conductor disponible: {reason}"),
            (attempt.Outcome, attempt.IsRejected, attempt.AttemptedBy, attempt.Detail));
        Assert.Equal(["driver-unavailable"], attempt.RuleIds);
        Assert.Null(attempt.ForcedReason);
    }

    /// <summary>It also breaks CE 561/2006 (11 h at the wheel), but that one could be forced and this one cannot (SPEC 0066 R6).</summary>
    [Fact]
    public async Task Allocate_OnLeaveAndIllegal_SaysTheLeaveAndOffersNoForcing()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 6, 11);
        await LeaveAsync(fleet.Drivers[0], 0, 24);

        var body = await JsonAsync(await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]));

        Assert.Equal("El conductor no está disponible para este servicio", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("forcible", out _));
        Assert.Equal(AllocationOutcome.RejectedDriver, await OutcomeAsync(trip));

        // Without the leave the same request is the usual forcible rejection.
        var id = (await JsonAsync(await _api.Client.GetAsync($"/api/drivers/{fleet.Drivers[0]}"))).GetProperty("Unavailabilities")[0].GetProperty("Id").GetInt32();
        await _api.Client.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities/{id}");
        Assert.True((await JsonAsync(await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]))).GetProperty("forcible").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0], force: true)).StatusCode);
    }

    [Fact]
    public async Task Allocate_Inactive_IsRejected_ForcedOrNot_AndReactivatedIsAccepted()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        await _api.Client.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}");

        foreach (var force in new[] { false, true })
        {
            var body = await JsonAsync(await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0], force));
            Assert.Equal((422, "Conductor no activo"), (body.GetProperty("status").GetInt32(), body.GetProperty("detail").GetString()));
        }

        await using (var write = _db.CreateContext())
        {
            Assert.All(await write.AllocationAttempts.Where(a => a.TripId == trip).ToListAsync(), a => Assert.Equal(["driver-active"], a.RuleIds));
            (await write.Drivers.SingleAsync(d => d.Id == fleet.Drivers[0])).Reactivate();
            await write.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0])).StatusCode);
    }

    /// <summary>What was assigned stays; changing only the vehicle is rejected too; a substitute is accepted (SPEC 0066 R9).</summary>
    [Fact]
    public async Task Allocate_AlreadyAssignedToSomeoneNowOnLeave_StaysUntilASubstituteIsAssigned()
    {
        var fleet = await FleetAsync(_db, _day, 2, 2);
        var trip = await TripAsync(_db, _day, fleet, 10, 2, driver: 0, vehicle: 0);
        await LeaveAsync(fleet.Drivers[0], 0, 24);
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Of(await ReadAsync(trip)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[1])).StatusCode);
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Of(await ReadAsync(trip)));

        Assert.Equal(HttpStatusCode.OK, (await Allocate(_api.Client, trip, fleet.Drivers[1], fleet.Vehicles[0])).StatusCode);
        Assert.Equal((fleet.Drivers[1], fleet.Vehicles[0]), Of(await ReadAsync(trip)));
    }

    /// <summary>Allocating, the search and the Control Center give the same answer about the same driver and service (SPEC 0066 R1, R7).</summary>
    [Theory]
    [InlineData("leave", true)]
    [InlineData("leave-until-departure", false)]
    [InlineData("inactive", true)]
    [InlineData("nothing", false)]
    public async Task AllocateSearchAndControlCenter_Agree(string kind, bool unavailable)
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var assigned = await TripAsync(_db, _day, fleet, 10, 2, driver: 0, vehicle: 0);
        var free = await TripAsync(_db, _day, fleet, 10, 2);
        if (kind == "leave") await LeaveAsync(fleet.Drivers[0], 11, 24);
        if (kind == "leave-until-departure") await LeaveAsync(fleet.Drivers[0], 0, 10);
        if (kind == "inactive") Assert.True((await _api.Client.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}")).IsSuccessStatusCode);

        var incidents = await JsonAsync(await _api.Client.GetAsync($"/api/control/alerts?baseId=1&date={_day:s}Z&pageSize=100"));
        var gaps = incidents.GetProperty("Items").EnumerateArray()
            .Where(a => a.GetProperty("Type").GetString() == "UnavailabilityGap" && a.GetProperty("EntityType").GetString() == "Driver").ToList();
        Assert.Equal(unavailable ? [fleet.Drivers[0]] : [], gaps.Select(a => a.GetProperty("EntityId").GetInt32()));
        Assert.All(gaps, a => Assert.Equal(("Buscar sustituto", $"No disponible en el servicio {assigned}"),
            (a.GetProperty("SuggestedAction").GetString(), a.GetProperty("Detail").GetString())));
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Of(await ReadAsync(assigned))); // nothing is taken away

        await using (var write = _db.CreateContext())
        {
            // Not the overlap with the other service: that one is cancelled for the question about the free one.
            (await write.Trips.SingleAsync(t => t.Id == assigned)).Cancel();
            await write.SaveChangesAsync();
        }

        var options = await JsonAsync(await _api.Client.GetAsync($"/api/trips/{free}/driver-options"));
        Assert.Equal(!unavailable, options.GetProperty("Candidates").EnumerateArray().Any(c => c.GetProperty("DriverId").GetInt32() == fleet.Drivers[0]));
        var allocation = await Allocate(_api.Client, free, fleet.Drivers[0], fleet.Vehicles[0]);
        Assert.Equal(unavailable ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK, allocation.StatusCode);
        if (kind == "leave")
        {
            var discarded = options.GetProperty("Discarded").EnumerateArray().Single(d => d.GetProperty("DriverId").GetInt32() == fleet.Drivers[0]);
            Assert.Equal((await JsonAsync(allocation)).GetProperty("detail").GetString(), discarded.GetProperty("Reason").GetString());
        }
    }

    /// <summary>One more kind of block, in the same figures as the others (SPEC 0066 R5).</summary>
    [Fact]
    public async Task Rejection_CountsAsBlockedInEveryFigure()
    {
        var fleet = await FleetAsync(_db, _day, 1, 1);
        var trip = await TripAsync(_db, _day, fleet, 10, 2);
        await LeaveAsync(fleet.Drivers[0], 0, 24);
        await Allocate(_api.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]);

        var now = DateTime.UtcNow;
        var period = $"baseId=1&from={now.AddHours(-1):s}Z&to={now.AddHours(1):s}Z";
        var metrics = await JsonAsync(await _api.Client.GetAsync($"/api/control/engine-metrics?{period}"));
        Assert.Equal((1, 1), (metrics.GetProperty("IllegalBlocked").GetInt32(), metrics.GetProperty("BlockedByReason").GetProperty("RejectedDriver").GetInt32()));
        var register = await JsonAsync(await _api.Client.GetAsync($"/api/engine/attempts?kind=Blocked&rule=driver-unavailable&baseId=1"));
        Assert.Equal((1, "RejectedDriver"), (register.GetProperty("Items").GetArrayLength(), register.GetProperty("Items")[0].GetProperty("Outcome").GetString()));
        Assert.Equal((1, 0, 1, 0), (Count("Validations"), Count("Accepted"), Count("Blocked"), Count("Forced")));
        var activity = await JsonAsync(await _api.Client.GetAsync($"/api/control/activity?baseId=1&kind=Blocked"));
        Assert.Equal("RejectedDriver", activity.GetProperty("Items")[0].GetProperty("Outcome").GetString());

        int Count(string name) => register.GetProperty("Counts").GetProperty(name).GetInt32();
    }

    private async Task LeaveAsync(int driver, double from, double to)
    {
        var response = await _api.Client.PostAsJsonAsync($"/api/drivers/{driver}/unavailabilities",
            new { Start = _day.AddHours(from), End = _day.AddHours(to), Type = "SickLeave" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Trip> ReadAsync(int id)
    {
        await using var read = _db.CreateContext();
        return await read.Trips.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<AllocationOutcome> OutcomeAsync(int trip)
    {
        await using var read = _db.CreateContext();
        return (await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == trip)).Outcome;
    }

    private static (int?, int?) Of(Trip trip) => (trip.AssignedDriverId, trip.AssignedVehicleId);
}
