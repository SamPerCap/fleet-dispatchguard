using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Recommendation;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// The copy in memory answers like the repositories, what is simulated is what assigning decides, proposing leaves no trace, and
/// applying rejects only the lines that changed meanwhile (SPEC 0062).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RecommendationIntegrationTests : IClassFixture<ConcurrencyDatabase>, IAsyncLifetime, IDisposable
{
    private static readonly DateTime Day = new(2043, 3, 2, 0, 0, 0, DateTimeKind.Utc);
    private const string Period = "2043-03-01T00:00:00Z", PeriodEnd = "2043-04-01T00:00:00Z";

    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _a;

    public RecommendationIntegrationTests(ConcurrencyDatabase db)
    {
        _db = db;
        _a = new ApiInstance(db.ConnectionString, lockWaitMilliseconds: 1_000);
    }

    public void Dispose() => _a.Dispose();

    /// <summary>Each test starts with nobody to recommend and nothing pending but what it creates.</summary>
    public async Task InitializeAsync()
    {
        await using var write = _db.CreateContext();
        await write.Drivers.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await write.Vehicles.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await write.Trips.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.Status, TripStatus.Cancelled));
        await write.AllocationAttempts.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Copy_AnswersLikeTheRepositories(int seed)
    {
        var world = await RandomWorldAsync(seed);
        await using var db = _db.CreateContext();
        var trips = new TripRepository(db);
        var records = new DrivingRecordRepository(db);
        var snapshot = await new PlanningSnapshotSource(db).LoadAsync(Day.AddDays(-60), Day.AddDays(60), [], default);
        var copy = new SimulatedTrips(snapshot.Trips);
        var random = new Random(seed);

        for (var i = 0; i < 60; i++)
        {
            var from = Day.AddHours(random.Next(-200, 200)); var to = from.AddHours(random.Next(0, 200));
            var driver = world.Drivers[random.Next(world.Drivers.Length)]; var vehicle = world.Vehicles[random.Next(world.Vehicles.Length)];
            var exclude = random.Next(2) == 0 ? (int?)null : world.All[random.Next(world.All.Length)];
            var (page, size) = random.Next(3) == 0 ? (2, 3) : (1, 500);

            var expected = await trips.GetByDriverAsync(driver, from, to, page, size, exclude);
            var actual = await copy.GetByDriverAsync(driver, from, to, page, size, exclude);
            Assert.Equal(expected.Items.Select(Row), actual.Items.Select(Row));
            Assert.Equal(expected.TotalCount, actual.TotalCount);
            Assert.Equal((await trips.GetByVehicleAsync(vehicle, from, to, page, size, exclude)).Items.Select(Row),
                (await copy.GetByVehicleAsync(vehicle, from, to, page, size, exclude)).Items.Select(Row));
            Assert.Equal((await trips.GetByVehicleAsync(vehicle, null, null, 1, int.MaxValue)).Items.Select(Row),
                (await copy.GetByVehicleAsync(vehicle, null, null, 1, int.MaxValue)).Items.Select(Row));
            Assert.Equal((await records.GetRecordsAsync(driver, from, to)).Select(p => (p.Start, p.End, p.TripId)),
                (await copy.GetRecordsAsync(driver, from, to)).Select(p => (p.Start, p.End, p.TripId)));
        }

        foreach (var booking in snapshot.Trips.Select(t => t.BookingId).Distinct())
        {
            Assert.Equal((await trips.GetByBookingAsync(booking)).Select(Row), (await copy.GetByBookingAsync(booking)).Select(Row));
        }

        Assert.Contains(snapshot.Trips, t => t.Status == TripStatus.Cancelled && t.AssignedDriverId is not null);
        Assert.Equal((await new DriverRepository(db).GetAllAsync()).Select(d => (d.Id, d.Unavailabilities.Count)).Order(),
            (await new SimulatedDrivers(snapshot.Drivers).GetAllAsync()).Select(d => (d.Id, d.Unavailabilities.Count)).Order());
        Assert.Equal((await new VehicleRepository(db).GetAllAsync()).Select(v => (v.Id, v.Unavailabilities.Count)).Order(),
            (await new SimulatedVehicles(snapshot.Vehicles).GetAllAsync()).Select(v => (v.Id, v.Unavailabilities.Count)).Order());

        static (int, int?, int?, DateTime, DateTime, TripStatus) Row(Trip trip) =>
            (trip.Id, trip.AssignedDriverId, trip.AssignedVehicleId, trip.ScheduledWindow.Start, trip.ScheduledWindow.End, trip.Status);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public async Task Proposal_IsAcceptedWholeWhenNothingChanged(int seed)
    {
        var world = await RandomWorldAsync(seed);
        var lines = await ProposeAsync();
        Assert.Equal(world.Pending.Order(), lines.Select(l => l.Trip).Order());
        var proposed = lines.Where(l => l.Status == "Proposed").ToList();
        Assert.True(proposed.Count >= 10, $"Only {proposed.Count} proposed");

        // Checking it untouched says the same, also about arriving.
        Assert.Equal(lines.Select(l => (l.Trip, l.Status)), (await CheckAsync(lines)).Select(l => (l.Trip, l.Status)));

        foreach (var line in proposed)
        {
            await ExpectAsync(HttpStatusCode.OK, await ApplyAsync(line));
        }

        await using var read = _db.CreateContext();
        var saved = await read.Trips.AsNoTracking().Where(t => world.Pending.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        Assert.All(lines, l => Assert.Equal((l.Driver, l.Vehicle), (saved[l.Trip].AssignedDriverId, saved[l.Trip].AssignedVehicleId)));
        Assert.All(proposed, l => Assert.True(saved[l.Trip].LegalValidation!.IsCompliant && !saved[l.Trip].IsForced));
        Assert.Equal(proposed.Count, await read.AllocationAttempts.CountAsync(x => x.Origin == AttemptOrigin.Recommended && x.Outcome == AllocationOutcome.Assigned));
        Assert.Equal(proposed.Count, await read.AllocationAttempts.CountAsync());
    }

    /// <summary>Any pair chosen by hand: the same verdict, with the same words, when it is checked and when it is applied.</summary>
    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public async Task Check_OfAnyPair_GivesTheVerdictOfApplyingIt(int seed)
    {
        var world = await RandomWorldAsync(seed);
        var random = new Random(seed);
        var lines = world.Pending.OrderBy(_ => random.Next())
            .Select(t => new Line(t, world.Drivers[random.Next(world.Drivers.Length)], world.Vehicles[random.Next(world.Vehicles.Length)], "", "", Manual: true)).ToList();

        var seen = new HashSet<AllocationOutcome>();
        foreach (var line in await CheckAsync(lines))
        {
            var response = await ApplyAsync(line);
            var (outcome, detail) = await LastAttemptAsync(line.Trip);
            seen.Add(outcome);
            Assert.True(line.Status == "Proposed" == (response.StatusCode == HttpStatusCode.OK), $"#{line.Trip} {line.Status} but {(int)response.StatusCode}: {line.Reason}");
            if (outcome != AllocationOutcome.Assigned) Assert.Equal(line.Reason, detail);
        }

        // The calendars are worth something: accepted and rejected for more than one reason.
        Assert.True(seen.Contains(AllocationOutcome.Assigned) && seen.Count >= 3, string.Join(", ", seen));
    }

    [Fact]
    public async Task Apply_AfterChanges_RejectsThoseLinesAndTheRestGoesOn()
    {
        var world = await PlainWorldAsync(drivers: 6, vehicles: 6, trips: 6); // all at the same time: one pair each
        var lines = await ProposeAsync();
        Assert.All(lines, l => Assert.Equal("Proposed", l.Status));
        Assert.Equal(6, lines.Select(l => l.Driver).Distinct().Count());
        await using (var write = _db.CreateContext())
        {
            // 0: someone else takes it · 1: the driver goes on leave · 2: cancelled · 3: locked
            write.Add(new DriverUnavailability(lines[1].Driver!.Value, new TimeWindow(Day, Day.AddDays(1)), UnavailabilityType.SickLeave));
            (await write.Trips.SingleAsync(t => t.Id == lines[2].Trip)).Cancel();
            (await write.Trips.SingleAsync(t => t.Id == lines[3].Trip)).Lock();
            await write.SaveChangesAsync();
        }

        await ExpectAsync(HttpStatusCode.OK, await _a.Client.AllocateAsync(
            lines[0].Trip, lines[0].Driver!.Value, lines[0].Vehicle!.Value, "Otra"));
        var results = new List<(HttpStatusCode Status, string? Detail)>();
        foreach (var line in lines)
        {
            var response = await ApplyAsync(line);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            results.Add((response.StatusCode, body.TryGetProperty("detail", out var detail) ? detail.GetString() : null));
        }

        Assert.Equal([HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.OK],
            results.Select(r => r.Status));
        Assert.Equal($"El servicio #{lines[0].Trip} ya está asignado", results[0].Detail);
        Assert.Equal($"El servicio #{lines[2].Trip} está cancelado", results[2].Detail);
        Assert.Equal($"El servicio #{lines[3].Trip} está bloqueado", results[3].Detail);

        await using var read = _db.CreateContext();
        var rows = await read.AllocationAttempts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.TripId, x.Outcome, x.Origin }).ToListAsync();
        Assert.Equal(
            [(lines[0].Trip, AllocationOutcome.Assigned, AttemptOrigin.Manual), (lines[1].Trip, AllocationOutcome.RejectedDriver, AttemptOrigin.Recommended),
             (lines[4].Trip, AllocationOutcome.Assigned, AttemptOrigin.Recommended), (lines[5].Trip, AllocationOutcome.Assigned, AttemptOrigin.Recommended)],
            rows.Select(x => (x.TripId, x.Outcome, x.Origin)));
        var cancelled = await read.Trips.SingleAsync(t => t.Id == lines[2].Trip);
        Assert.Equal((TripStatus.Cancelled, (int?)null), (cancelled.Status, cancelled.AssignedDriverId));

        // Checking the same proposal again says why, line by line, without touching anything.
        string[] reasons = ["ya está asignado", "Conductor disponible", "está cancelado", "está bloqueado", "ya está asignado", "ya está asignado"];
        var again = await CheckAsync(lines);
        Assert.All(again.Zip(reasons), x => Assert.Contains(x.Second, x.First.Reason));
    }

    /// <summary>Two people apply the same proposal at once, on two instances: the second does not reassign what the first did.</summary>
    [Fact]
    public async Task Apply_SameLineAtOnceOnTwoInstances_OnlyOneAssigns()
    {
        var world = await PlainWorldAsync(drivers: 2, vehicles: 2, trips: 1);
        using var oneInstance = new ApiInstance(_db.ConnectionString);
        using var otherInstance = new ApiInstance(_db.ConnectionString);
        var pause = oneInstance.Saves.PauseNext();
        var first = ApplyAsync(new Line(world.Pending[0], world.Drivers[0], world.Vehicles[0], "Proposed", ""), oneInstance.Client);
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var second = ApplyAsync(new Line(world.Pending[0], world.Drivers[1], world.Vehicles[1], "Proposed", ""), otherInstance.Client);
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30))) await _db.UntilWaitingAsync(1, stop.Token);
        pause.Release();

        await ExpectAsync(HttpStatusCode.OK, await first);
        Assert.Contains("ya está asignado", (await ExpectAsync(HttpStatusCode.Conflict, await second)).GetProperty("detail").GetString());
        await using var read = _db.CreateContext();
        Assert.Equal(world.Drivers[0], (await read.Trips.SingleAsync(t => t.Id == world.Pending[0])).AssignedDriverId);
        Assert.Equal(1, await read.AllocationAttempts.CountAsync());
    }

    /// <summary>A proposal left open past the departure: applying says the same as checking.</summary>
    [Fact]
    public async Task Apply_DepartedOrNoLongerPending_IsRejectedAsWhenChecking()
    {
        var world = await PlainWorldAsync(drivers: 1, vehicles: 1, trips: 1);
        int departed;
        await using (var write = _db.CreateContext())
        {
            var place = await write.Locations.OrderByDescending(l => l.Id).FirstAsync();
            var trip = new Trip(place, place, new TimeWindow(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1)), 20, ServiceType.Regular);
            write.Add(trip);
            await write.Trips.Where(t => t.Id == world.Pending[0]).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TripStatus.Completed));
            await write.SaveChangesAsync();
            departed = trip.Id;
        }

        foreach (var (trip, reason) in new[] { (departed, "ya ha salido"), (world.Pending[0], "ya no está pendiente") })
        {
            var line = new Line(trip, world.Drivers[0], world.Vehicles[0], "", "");
            Assert.Contains(reason, Assert.Single(await CheckAsync([line])).Reason);
            Assert.Contains(reason, (await ExpectAsync(HttpStatusCode.Conflict, await ApplyAsync(line))).GetProperty("detail").GetString());
        }

        // Too far apart for a proposal: reading everything in between is not what checking is for.
        await ExpectAsync(HttpStatusCode.BadRequest, await _a.Client.PostAsJsonAsync("/api/recommendations/check",
            new { Lines = new[] { new { TripId = departed }, new { TripId = world.Pending[0] } } }));
        await using var read = _db.CreateContext();
        Assert.Equal(0, await read.AllocationAttempts.CountAsync());

        // By hand, the panel still can: only recommending refuses.
        await ExpectAsync(HttpStatusCode.OK, await _a.Client.AllocateAsync(departed, world.Drivers[0], world.Vehicles[0]));
    }

    /// <summary>The 503 of SPEC 0065 means "try again", it is not a rejection: it leaves nothing behind.</summary>
    [Fact]
    public async Task Apply_WhileTheDriverIsBusy_IsRetryableAndNotAnAttempt()
    {
        await PlainWorldAsync(drivers: 1, vehicles: 1, trips: 1);
        var line = Assert.Single(await ProposeAsync());
        await using (await _db.HoldAsync($"lof:driver:{line.Driver}"))
        {
            Assert.True((await ExpectAsync(HttpStatusCode.ServiceUnavailable, await ApplyAsync(line))).GetProperty("retryable").GetBoolean());
            Assert.Equal("Proposed", Assert.Single(await ProposeAsync()).Status); // proposing waits for nobody
        }

        await using var read = _db.CreateContext();
        Assert.Equal(0, await read.AllocationAttempts.CountAsync());
        await ExpectAsync(HttpStatusCode.OK, await ApplyAsync(line));
        var activity = await ExpectAsync(HttpStatusCode.OK, await _a.Client.GetAsync($"/api/control/activity?since={DateTime.UtcNow.AddHours(-1):s}Z"));
        Assert.Equal("Recommended", Assert.Single(activity.GetProperty("Items").EnumerateArray()).GetProperty("Origin").GetString());
    }

    [Fact]
    public async Task ProposeCheckOptionsAndBadge_WriteNothing()
    {
        await RandomWorldAsync(31);
        var before = await StateAsync();
        var lines = await ProposeAsync();
        await CheckAsync(lines);
        await ExpectAsync(HttpStatusCode.OK, await _a.Client.PostAsJsonAsync("/api/recommendations/options", new { TripId = lines[0].Trip, Lines = lines.Select(Input) }));
        await ExpectAsync(HttpStatusCode.OK, await _a.Client.GetAsync($"/api/trips/{lines[0].Trip}/recommendation"));
        Assert.Equal(before, await StateAsync());

        async Task<string> StateAsync()
        {
            await using var read = _db.CreateContext();
            var trips = await read.Trips.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
            return $"{await read.AllocationAttempts.CountAsync()}|" + string.Join(";", trips.Select(t =>
                $"{t.Id}:{t.Status}:{t.AssignedDriverId}:{t.AssignedVehicleId}:{t.IsLocked}:{t.Audit.UpdatedAt:O}:{t.LegalValidation?.ValidatedAt:O}"));
        }
    }

    /// <summary>The copy reaches as far as the rules look: 4 h just before or just after a 4 h service leave no break.</summary>
    [Theory]
    [InlineData(3.75)]
    [InlineData(12)]
    public async Task Propose_SeesTheServicesAroundTheOnesAskedFor(double heldStart)
    {
        var world = await PlainWorldAsync(drivers: 2, vehicles: 3, trips: 0);
        int trip;
        await using (var write = _db.CreateContext())
        {
            var place = await write.Locations.OrderByDescending(l => l.Id).FirstAsync();
            var first = await write.Drivers.SingleAsync(d => d.Id == world.Drivers[0]);
            var held = new Trip(place, place, new TimeWindow(Day.AddHours(heldStart), Day.AddHours(heldStart + 4)), 20, ServiceType.Regular);
            held.AssignResources(first, await write.Vehicles.SingleAsync(v => v.Id == world.Vehicles[2]));
            var pending = new Trip(place, place, new TimeWindow(Day.AddHours(8), Day.AddHours(12)), 20, ServiceType.Regular);
            write.AddRange(held, pending);
            await write.SaveChangesAsync();
            trip = pending.Id;
        }

        var line = Assert.Single(await ProposeAsync());

        Assert.Equal((trip, world.Drivers[1]), (line.Trip, line.Driver!.Value));
        await ExpectAsync(HttpStatusCode.UnprocessableEntity, await ApplyAsync(line with { Driver = world.Drivers[0] }));
    }

    [Fact]
    public async Task Propose_ByBooking_ByBaseAndByPeriod()
    {
        var world = await PlainWorldAsync(drivers: 4, vehicles: 4, trips: 3);
        int baseId, booking;
        await using (var write = _db.CreateContext())
        {
            var other = new OperatingBase($"R-{Guid.NewGuid():N}"[..20]);
            write.Bases.Add(other);
            await write.SaveChangesAsync();
            var trip = await write.Trips.SingleAsync(t => t.Id == world.Pending[1]);
            trip.MoveToBase(other.Id);
            await write.SaveChangesAsync();
            (baseId, booking) = (other.Id, (await write.Trips.SingleAsync(t => t.Id == world.Pending[2])).BookingId);
        }

        Assert.Equal([world.Pending[1]], (await ProposeAsync(new { BaseId = baseId, From = Period, To = PeriodEnd })).Select(l => l.Trip));
        Assert.Equal([world.Pending[2]], (await ProposeAsync(new { BookingId = booking })).Select(l => l.Trip));
        Assert.Equal(3, (await ProposeAsync()).Count);
        Assert.Empty(await ProposeAsync(new { From = "2043-03-02T10:00:00Z", To = PeriodEnd })); // the trips end at 10:00
        Assert.Equal(3, (await ProposeAsync(new { From = "2043-03-02T09:59:00Z", To = PeriodEnd })).Count);
        Assert.Empty(await ProposeAsync(new { From = Period, To = "2043-03-02T08:00:00Z" })); // and start at 8:00
        Assert.Equal(3, (await ProposeAsync(new { From = Period, To = "2043-03-02T09:01:00+01:00" })).Count);
    }

    [Theory]
    [InlineData("/api/recommendations", "{}", HttpStatusCode.BadRequest)]
    [InlineData("/api/recommendations", "{\"From\":\"2043-03-02T00:00:00Z\",\"To\":\"2043-03-02T00:00:00Z\"}", HttpStatusCode.BadRequest)]
    [InlineData("/api/recommendations", "{\"BookingId\":99999999}", HttpStatusCode.NotFound)]
    [InlineData("/api/recommendations", "{\"BaseId\":99999999,\"From\":\"2043-03-02T00:00:00Z\",\"To\":\"2043-03-03T00:00:00Z\"}", HttpStatusCode.NotFound)]
    [InlineData("/api/recommendations/check", "{\"Lines\":[{\"TripId\":99999999}]}", HttpStatusCode.NotFound)]
    [InlineData("/api/recommendations/check", "{\"Lines\":[{\"TripId\":1},{\"TripId\":1}]}", HttpStatusCode.BadRequest)]
    [InlineData("/api/recommendations/check", "{\"Lines\":[null]}", HttpStatusCode.BadRequest)]
    [InlineData("/api/recommendations/check", "{}", HttpStatusCode.OK)]
    [InlineData("/api/recommendations/options", "{\"TripId\":99999999}", HttpStatusCode.NotFound)]
    public async Task BadInput_HasItsCode(string url, string json, HttpStatusCode expected) =>
        await ExpectAsync(expected, await _a.Client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json")));

    [Fact]
    public async Task Allocate_ForcedRecommendation_IsRejectedAndNotRecorded()
    {
        var world = await PlainWorldAsync(1, 1, 1);
        Task<HttpResponseMessage> From(string origin, bool force = false) => _a.Client.AllocateAsync(
            world.Pending[0], world.Drivers[0], world.Vehicles[0], "Eva", force, force ? "Motivo de prueba largo" : null, origin);
        await ExpectAsync(HttpStatusCode.BadRequest, await From("Recommended", force: true));
        Assert.Equal(HttpStatusCode.BadRequest, (await From("Nadie")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await From("7")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _a.Client.GetAsync("/api/trips/99999999/recommendation")).StatusCode);
        await using var read = _db.CreateContext();
        Assert.Equal(0, await read.AllocationAttempts.CountAsync());
    }

    [Fact]
    public async Task Register_FiltersByOrigin_AndTheCountsStillAddUp()
    {
        await using (var write = _db.CreateContext())
        {
            AllocationAttempt At(int minutes, AllocationOutcome outcome, AttemptOrigin origin) =>
                new(Day.AddHours(9).AddMinutes(minutes), "Eva", 1, 1, 1, 1, outcome, null, [], outcome == AllocationOutcome.Forced ? "Motivo de prueba" : null, null, origin);
            write.AllocationAttempts.AddRange(
                At(0, AllocationOutcome.Assigned, AttemptOrigin.Recommended), At(1, AllocationOutcome.Assigned, AttemptOrigin.Recommended),
                At(2, AllocationOutcome.RejectedSchedule, AttemptOrigin.Recommended), At(3, AllocationOutcome.Assigned, AttemptOrigin.Manual),
                At(4, AllocationOutcome.RejectedCompliance, AttemptOrigin.Manual), At(5, AllocationOutcome.Forced, AttemptOrigin.Manual));
            await write.SaveChangesAsync();
        }

        var url = "/api/engine/attempts?date=2043-03-02T12:00:00Z&pageSize=50";
        Assert.Equal((6, 3, 2, 1, 6), await CountsAsync(url));
        Assert.Equal((3, 2, 1, 0, 3), await CountsAsync($"{url}&origin=Recommended"));
        Assert.Equal((3, 1, 1, 1, 3), await CountsAsync($"{url}&origin=Manual"));
        Assert.Equal((3, 2, 1, 0, 2), await CountsAsync($"{url}&origin=Recommended&kind=Assigned")); // the result never changes the counts
        var hourly = await ExpectAsync(HttpStatusCode.OK, await _a.Client.GetAsync("/api/engine/hourly?date=2043-03-02T12:00:00Z&origin=Recommended"));
        Assert.Equal(3, hourly.GetProperty("Hours").EnumerateArray().Sum(h => h.GetProperty("Accepted").GetInt32() + h.GetProperty("Blocked").GetInt32() + h.GetProperty("Forced").GetInt32()));

        async Task<(int, int, int, int, int)> CountsAsync(string query)
        {
            var page = await ExpectAsync(HttpStatusCode.OK, await _a.Client.GetAsync(query));
            var counts = page.GetProperty("Counts");
            if (query.Contains("origin=Recommended"))
                Assert.All(page.GetProperty("Items").EnumerateArray(), i => Assert.Equal("Recommended", i.GetProperty("Origin").GetString()));
            return (counts.GetProperty("Validations").GetInt32(), counts.GetProperty("Accepted").GetInt32(), counts.GetProperty("Blocked").GetInt32(), counts.GetProperty("Forced").GetInt32(),
                page.GetProperty("TotalCount").GetInt32());
        }
    }

    [Fact]
    public async Task AttemptWithoutOrigin_IsManual()
    {
        await using var db = _db.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AllocationAttempts" ("AttemptedAt", "AttemptedBy", "TripId", "DriverId", "VehicleId", "BaseId", "Outcome", "RuleIds", "CreatedAt")
            VALUES ('2043-01-01Z', 'Eva sin origen', 1, 1, 1, 1, 'Assigned', ARRAY[]::text[], '2043-01-01Z'),
                   ('2043-01-02Z', 'Eva sin origen', 2, 1, 1, 1, 'RejectedSchedule', ARRAY[]::text[], '2043-01-02Z')
            """);

        Assert.Equal([AttemptOrigin.Manual, AttemptOrigin.Manual],
            await db.AllocationAttempts.Where(x => x.AttemptedBy == "Eva sin origen").Select(x => x.Origin).ToListAsync());
    }

    private sealed record Line(int Trip, int? Driver, int? Vehicle, string Status, string Reason, bool Manual = false);

    private sealed record World(int[] Drivers, int[] Vehicles, int[] Pending, int[] All);

    private static object Input(Line line) => new { TripId = line.Trip, DriverId = line.Driver, VehicleId = line.Vehicle, line.Manual };

    private async Task<IReadOnlyList<Line>> ProposeAsync(object? request = null) =>
        Lines((await ExpectAsync(HttpStatusCode.OK, await _a.Client.PostAsJsonAsync("/api/recommendations", request ?? new { From = Period, To = PeriodEnd }))).GetProperty("Lines"), null);

    private async Task<IReadOnlyList<Line>> CheckAsync(IReadOnlyList<Line> lines) =>
        Lines(await ExpectAsync(HttpStatusCode.OK, await _a.Client.PostAsJsonAsync("/api/recommendations/check", new { Lines = lines.Select(Input) })), lines);

    private static List<Line> Lines(JsonElement json, IReadOnlyList<Line>? sent) => json.EnumerateArray().Select((l, i) => new Line(
        l.GetProperty("TripId").GetInt32(), Id(l, "Driver") ?? sent?[i].Driver, Id(l, "Vehicle") ?? sent?[i].Vehicle,
        l.GetProperty("Status").GetString()!, l.GetProperty("Reason").GetString()!, sent?[i].Manual ?? false)).ToList();

    private static int? Id(JsonElement line, string resource) =>
        line.GetProperty(resource) is { ValueKind: JsonValueKind.Object } reference ? reference.GetProperty("Id").GetInt32() : null;

    private Task<HttpResponseMessage> ApplyAsync(Line line, HttpClient? client = null) => (client ?? _a.Client).AllocateAsync(
        line.Trip, line.Driver!.Value, line.Vehicle!.Value, "Eva", origin: line.Manual ? "Manual" : "Recommended", onlyIfUnassigned: true);

    private async Task<(AllocationOutcome, string?)> LastAttemptAsync(int trip)
    {
        await using var read = _db.CreateContext();
        var last = await read.AllocationAttempts.Where(x => x.TripId == trip).OrderByDescending(x => x.Id).FirstAsync();
        return (last.Outcome, last.Detail);
    }

    private static async Task<JsonElement> ExpectAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static Location At(double km) =>
        new($"km {km:0} {Guid.NewGuid():N}"[..16], "Calle 1", new GeoCoordinate(40m + (decimal)(km / 111.19492664), -3.7m), "Madrid", "Madrid");

    /// <summary>Everybody at the departure and every trip 8:00–10:00 of the same day.</summary>
    private async Task<World> PlainWorldAsync(int drivers, int vehicles, int trips)
    {
        await using var write = _db.CreateContext();
        var place = At(0);
        var newDrivers = Enumerable.Range(0, drivers).Select(_ => TripRepositoryTests.NewDriver()).ToArray();
        var newVehicles = Enumerable.Range(0, vehicles).Select(_ => TripRepositoryTests.NewVehicle()).ToArray();
        write.Add(place); write.AddRange(newDrivers); write.AddRange(newVehicles);
        await write.SaveChangesAsync();
        foreach (var driver in newDrivers) driver.SetHomeLocation(place);
        foreach (var vehicle in newVehicles) vehicle.SetHomeLocation(place);
        var newTrips = Enumerable.Range(0, trips)
            .Select(_ => new Trip(place, place, new TimeWindow(Day.AddHours(8), Day.AddHours(10)), 20, ServiceType.Regular)).ToArray();
        write.AddRange(newTrips);
        await write.SaveChangesAsync();
        return new World([.. newDrivers.Select(x => x.Id)], [.. newVehicles.Select(x => x.Id)], [.. newTrips.Select(x => x.Id)], [.. newTrips.Select(x => x.Id)]);
    }

    /// <summary>Calendars with past and future services, leaves, workshops, cancelled services, small buses and bookings of several days.</summary>
    private async Task<World> RandomWorldAsync(int seed)
    {
        var random = new Random(seed);
        await using var write = _db.CreateContext();
        var places = Enumerable.Range(0, 8).Select(_ => At(random.Next(0, 45))).ToArray();
        var drivers = Enumerable.Range(0, 9).Select(_ => TripRepositoryTests.NewDriver()).ToArray();
        var vehicles = Enumerable.Range(0, 8).Select(i => new Vehicle($"PL-{Guid.NewGuid():N}"[..12], "Irizar", "i6", i % 3 == 0 ? 20 : 55)).ToArray();
        write.AddRange(places); write.AddRange(drivers); write.AddRange(vehicles);
        await write.SaveChangesAsync();
        Location Place() => places[random.Next(places.Length)];
        foreach (var driver in drivers.Skip(1)) driver.SetHomeLocation(Place());
        foreach (var vehicle in vehicles.Skip(1)) vehicle.SetHomeLocation(Place());
        write.Add(new DriverUnavailability(drivers[2].Id, new TimeWindow(Day.AddDays(1), Day.AddDays(3)), UnavailabilityType.Vacation));
        write.Add(new VehicleUnavailability(vehicles[2].Id, new TimeWindow(Day, Day.AddDays(2)), VehicleUnavailabilityType.Repair));

        // Two weeks of history and some services ahead: one a day per driver at most, so most are legal.
        var held = new List<Trip>();
        for (var day = -13; day < 8; day++)
        {
            foreach (var index in Enumerable.Range(0, drivers.Length).Where(_ => random.Next(100) < (day < 0 ? 45 : 25)))
            {
                var start = Day.AddDays(day).AddHours(5 + random.Next(0, 12));
                var trip = new Trip(Place(), Place(), new TimeWindow(start, start.AddHours(1 + random.Next(0, 5))), 15, ServiceType.Regular);
                trip.AssignResources(drivers[index], vehicles[random.Next(vehicles.Length)]);
                if (random.Next(10) == 0) trip.Cancel();
                else if (day < 0) trip.Complete();
                held.Add(trip);
            }
        }

        var pending = new List<Trip>();
        for (var b = 0; b < 14; b++)
        {
            var booking = new Booking($"Cliente {b}", "Servicio");
            for (var n = random.Next(1, 4); n > 0; n--)
            {
                var start = Day.AddDays(random.Next(0, 6)).AddHours(5).AddMinutes(30 * random.Next(0, 30));
                pending.Add(new Trip(Place(), Place(), new TimeWindow(start, start.AddMinutes(30 * random.Next(1, 9))), random.Next(2) == 0 ? 18 : 40, ServiceType.Discretionary, null, booking));
            }
        }

        write.AddRange(held); write.AddRange(pending);
        await write.SaveChangesAsync();
        return new World([.. drivers.Select(x => x.Id)], [.. vehicles.Select(x => x.Id)], [.. pending.Select(x => x.Id)], [.. held.Concat(pending).Select(x => x.Id)]);
    }
}
