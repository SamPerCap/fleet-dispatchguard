using System.Net;
using System.Text.Json;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EngineAuditIntegrationTests : IDisposable
{
    // 10:00 in Madrid (UTC+1) on a day no other test uses.
    private static readonly DateTime Day = new(2037, 2, 10, 9, 0, 0, DateTimeKind.Utc);
    private const int DriverA = 910_001, DriverB = 910_002, VehicleA = 920_001, VehicleB = 920_002;

    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EngineAuditIntegrationTests(PostgreSqlFixture db)
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

    /// <summary>A base of its own with 9 attempts on <see cref="Day"/>, one the day before and one in another base.</summary>
    private async Task<int> SeedAsync()
    {
        await using var write = _db.CreateContext();
        var mine = new OperatingBase($"M-{Guid.NewGuid():N}"[..20]);
        var other = new OperatingBase($"O-{Guid.NewGuid():N}"[..20]);
        write.Bases.AddRange(mine, other);
        await write.SaveChangesAsync();

        AllocationAttempt At(int minutes, string who, int driver, int vehicle, AllocationOutcome outcome, params string[] rules) =>
            new(Day.AddMinutes(minutes), who, 1, driver, vehicle, mine.Id, outcome, outcome == AllocationOutcome.Assigned ? null : "motivo",
                rules, outcome == AllocationOutcome.Forced ? "Único conductor disponible a esa hora" : null);

        write.AllocationAttempts.AddRange(
            At(0, "Lucía", DriverA, VehicleA, AllocationOutcome.Assigned),
            At(5, "Lucía", DriverA, VehicleB, AllocationOutcome.RejectedCompliance, "daily", "continuous"),
            At(10, "Pablo", DriverB, VehicleA, AllocationOutcome.RejectedCompliance, "weekly-rest"),
            At(15, "Pablo", DriverB, VehicleB, AllocationOutcome.RejectedVehicle, "vehicle-seats"),
            At(70, "Lucía", DriverA, VehicleA, AllocationOutcome.RejectedSchedule, "driver-overlap"),
            At(75, "Pablo", DriverA, VehicleB, AllocationOutcome.Forced, "daily"),
            At(80, "Lucía", DriverB, VehicleA, AllocationOutcome.Assigned),
            At(200, "Pablo", DriverB, VehicleB, AllocationOutcome.Assigned),
            At(205, "Ana", DriverA, VehicleA, AllocationOutcome.Forced, "continuous"),
            // The day before (only in the 7-day period).
            At(-24 * 60, "Ana", DriverA, VehicleA, AllocationOutcome.RejectedCompliance, "daily"),
            // Another base (never in this base's figures).
            new AllocationAttempt(Day.AddMinutes(1), "Lucía", 1, DriverA, VehicleA, other.Id, AllocationOutcome.Assigned));
        await write.SaveChangesAsync();
        return mine.Id;
    }

    private static EngineAuditService Service(DispatchGuardDbContext context) => new(new AllocationAttemptAudit(context));

    public static TheoryData<string?, int?, int?, string?> Filters => new()
    {
        { null, null, null, null },
        { "daily", null, null, null },
        { "continuous", null, null, null },
        { null, DriverA, null, null },
        { null, null, VehicleB, null },
        { null, null, null, "Pablo" },
        { "daily", DriverA, VehicleB, null },
        { "vehicle-active", null, null, null },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Attempts_EachKpiEqualsTheRowsOfTheRegisterFilteredByThatResult(string? rule, int? driverId, int? vehicleId, string? by)
    {
        var baseId = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = Service(context);
        EngineAuditQuery Query(ActivityKind kind) => new(Day, 1, baseId, kind, rule, driverId, vehicleId, by);

        var all = await service.AttemptsAsync(Query(ActivityKind.All), 1, 100);
        var assigned = await service.AttemptsAsync(Query(ActivityKind.Assigned), 1, 100);
        var blocked = await service.AttemptsAsync(Query(ActivityKind.Blocked), 1, 100);
        var forced = await service.AttemptsAsync(Query(ActivityKind.Forced), 1, 100);

        Assert.Equal(all.Counts.Validations, all.TotalCount);
        Assert.Equal(all.Counts.Accepted, assigned.TotalCount);
        Assert.Equal(all.Counts.Blocked, blocked.TotalCount);
        Assert.Equal(all.Counts.Forced, forced.TotalCount);
        Assert.Equal(all.Counts.Accepted + all.Counts.Blocked + all.Counts.Forced, all.Counts.Validations);
        // The KPIs do not depend on the result filter.
        Assert.All([assigned, blocked, forced], p => Assert.Equal(all.Counts, p.Counts));
        // Every row honours every filter.
        Assert.All(all.Items, i =>
        {
            Assert.Equal(baseId, i.BaseId);
            if (rule is not null) Assert.Contains(rule, i.RuleIds);
            if (driverId is not null) Assert.Equal(driverId, i.DriverId);
            if (vehicleId is not null) Assert.Equal(vehicleId, i.VehicleId);
            if (by is not null) Assert.Equal(by, i.Who);
        });
    }

    [Fact]
    public async Task Attempts_KnownFigures_AndRuleFilterReadsTheJsonColumn()
    {
        var baseId = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = Service(context);

        var all = await service.AttemptsAsync(new EngineAuditQuery(Day, 1, baseId), 1, 100);
        Assert.Equal(new EngineCountsDto(9, 3, 4, 2), all.Counts);
        Assert.Equal(["Ana", "Lucía", "Pablo"], all.People);

        var daily = await service.AttemptsAsync(new EngineAuditQuery(Day, 1, baseId, Rule: "daily"), 1, 100);
        Assert.Equal(new EngineCountsDto(2, 0, 1, 1), daily.Counts);
        var forced = Assert.Single(daily.Items, i => i.Outcome == AllocationOutcome.Forced);
        Assert.Equal("Único conductor disponible a esa hora", forced.ForcedReason);

        var week = await service.AttemptsAsync(new EngineAuditQuery(Day, 7, baseId, Rule: "daily"), 1, 100);
        Assert.Equal(3, week.TotalCount);
        Assert.Equal(week.To.AddDays(-7), week.From);
    }

    [Fact]
    public async Task Attempts_Paging_NewestFirstWithoutGapsOrRepeats()
    {
        var baseId = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = Service(context);
        var query = new EngineAuditQuery(Day, 1, baseId);

        var pages = new List<EngineAuditPageDto>();
        for (var p = 1; p <= 3; p++) pages.Add(await service.AttemptsAsync(query, p, 4));

        Assert.All(pages, p => { Assert.Equal(9, p.TotalCount); Assert.Equal(3, p.TotalPages); });
        Assert.Equal([4, 4, 1], pages.Select(p => p.Items.Count));
        var rows = pages.SelectMany(p => p.Items).ToList();
        Assert.Equal(9, rows.Select(r => r.Id).Distinct().Count());
        Assert.Equal(rows.OrderByDescending(r => r.At).ThenByDescending(r => r.Id).Select(r => r.Id), rows.Select(r => r.Id));
        Assert.Empty((await service.AttemptsAsync(query, 4, 4)).Items);
    }

    [Fact]
    public async Task Hourly_AddsUpToTheValidationsWithTheSameFilters()
    {
        var baseId = await SeedAsync();
        await using var context = _db.CreateContext();
        var service = Service(context);

        foreach (var (days, by) in new (int, string?)[] { (1, null), (1, "Lucía"), (7, null) })
        {
            var query = new EngineAuditQuery(Day, days, baseId, By: by);
            var hourly = await service.HourlyAsync(query);
            var page = await service.AttemptsAsync(query, 1, 1);

            Assert.Equal(page.Counts.Validations, hourly.Hours.Sum(h => h.Validations));
            Assert.Equal(page.Counts.Blocked, hourly.Hours.Sum(h => h.Blocked));
            Assert.Equal(page.Counts.Forced, hourly.Hours.Sum(h => h.Forced));
            Assert.Equal(days * 24, hourly.Hours.Count);
        }
    }

    [Fact]
    public async Task AllocateDriver_Rejected_LeavesRulesAndExplanationForTheAudit()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2037, 3, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: true);
        VehicleIneligibleException rejection;

        await using (var context = _db.CreateContext())
        {
            var uow = new UnitOfWork(context);
            var allocation = new DriverAllocationService(new LegalValidationService(new DrivingRecordRepository(context)), new VehicleEligibilityService(),
                new ScheduleConflictService(new TripRepository(context)), uow, new AllocationAttemptLog(context));
            rejection = await Assert.ThrowsAsync<VehicleIneligibleException>(async () => await allocation.AllocateDriver(tripId, driverId, vehicleId, default, "Lucía"));
        }

        await using var read = _db.CreateContext();
        var id = (await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId)).Id;
        var detail = await Service(read).AttemptAsync(id);

        Assert.Equal(AllocationOutcome.RejectedVehicle, detail.Attempt.Outcome);
        Assert.Equal(["vehicle-unavailable"], detail.Attempt.RuleIds);
        Assert.True(detail.Attempt.HasExplanation);
        Assert.Null(detail.Attempt.ForcedReason);
        Assert.Equal(rejection.Explanation!.Summary, detail.Explanation!.Summary);
        Assert.Equal(rejection.Explanation.Business, detail.Explanation.Business);
    }

    [Fact]
    public async Task Http_AttemptsWithKpis_UnknownRule400_UnknownAttempt404()
    {
        var baseId = await SeedAsync();

        var ok = await _client.GetAsync($"/api/engine/attempts?baseId={baseId}&date={Day:O}&kind=Blocked&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            var root = json.RootElement;
            Assert.Equal(4, root.GetProperty("Counts").GetProperty("Blocked").GetInt32());
            Assert.Equal(4, root.GetProperty("TotalCount").GetInt32());
            Assert.Equal(2, root.GetProperty("Items").GetArrayLength());
            Assert.All(root.GetProperty("Items").EnumerateArray(), i => Assert.StartsWith("Rejected", i.GetProperty("Outcome").GetString()));
        }

        var hourly = await _client.GetAsync($"/api/engine/hourly?baseId={baseId}&date={Day:O}&days=7");
        Assert.Equal(HttpStatusCode.OK, hourly.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/engine/attempts?baseId={baseId}&rule=no-such-rule")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/engine/attempts?baseId={baseId}&days=3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/engine/attempts/2147483647")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/engine/attempts?baseId=2147483647")).StatusCode);
    }
}
