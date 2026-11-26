using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0092: where PostgreSQL would behave in another way if nobody told it.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgreSqlBehaviourTests(PostgreSqlFixture server) : IAsyncLifetime
{
    private readonly ConcurrencyDatabase _db = new(server);

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    /// <summary>02:30 happens twice in Madrid on 2026-10-25: at 00:30 and at 01:30 UTC.</summary>
    [Theory]
    [InlineData(0, DateTimeKind.Utc)]
    [InlineData(1, DateTimeKind.Utc)]
    [InlineData(0, DateTimeKind.Unspecified)]
    [InlineData(1, DateTimeKind.Unspecified)]
    public async Task Instant_InTheRepeatedHour_IsReadBackAsTheSame(int hour, DateTimeKind kind)
    {
        var start = new DateTime(2026, 10, 25, hour, 30, 0, kind);
        var id = await SaveTripAsync(start);

        await using var read = _db.CreateContext();
        var trip = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == id);

        Assert.Equal(start.Ticks, trip.ScheduledWindow.Start.Ticks);
        Assert.Equal(1, await read.Trips.CountAsync(t => t.Id == id && t.ScheduledWindow.Start == start));
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds();
        Assert.Equal(seconds, await ScalarAsync($"""SELECT extract(epoch FROM "ScheduledWindowStart")::bigint FROM "Trips" WHERE "Id" = {id}"""));
    }

    [Fact]
    public async Task Instant_OfTheLocalClock_IsStoredAsTheSameInstant()
    {
        var utc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var id = await SaveTripAsync(utc.ToLocalTime());

        await using var read = _db.CreateContext();

        Assert.Equal(utc.Ticks, (await read.Trips.AsNoTracking().SingleAsync(t => t.Id == id)).ScheduledWindow.Start.Ticks);
    }

    [Fact]
    public async Task Columns_EveryInstantHasAZone_AndEveryTextIgnoresCase()
    {
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND data_type IN ('timestamp without time zone', 'date')"));

        await using var context = _db.CreateContext();
        var texts = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(string) && !property.DeclaringType.ContainingEntityType.IsMappedToJson())
            .ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, property => Assert.Equal("spanish_ci_as", property.GetCollation()));
    }

    [Theory]
    [InlineData("Martin", "MARTIN", false)]
    [InlineData("Martin", "Martín", true)]
    [InlineData("Espana", "España", true)]
    public async Task BaseName_IgnoresCaseAndTellsAccentsApart(string first, string second, bool bothFit)
    {
        await using var context = _db.CreateContext();
        context.Bases.Add(new OperatingBase(first));
        await context.SaveChangesAsync();
        context.Bases.Add(new OperatingBase(second));

        var refused = await Record.ExceptionAsync(() => context.SaveChangesAsync());

        Assert.Equal(bothFit, refused is null);
    }

    /// <summary>The domain writes them in capitals; a row that came in another way still counts.</summary>
    [Fact]
    public async Task PlateAndIdentification_DifferOnlyInCase_AreTaken()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.AddRange(new Vehicle("1234ABC", "Irizar", "i6", 55), new Driver("12345678Z", "Ana", "Pérez"));
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("""
                UPDATE "Vehicles" SET "LicensePlate" = '1234abc';
                UPDATE "Drivers" SET "IdentificationNumber" = '12345678z';
                """);
        }

        await using var context = _db.CreateContext();
        using var unitOfWork = new UnitOfWork(context);
        Assert.True(await unitOfWork.Vehicles.ExistsByLicensePlateAsync("1234ABC"));
        Assert.True(await unitOfWork.Drivers.ExistsByIdentificationNumberAsync("12345678Z"));
        context.Vehicles.Add(new Vehicle("1234ABC", "Irizar", "i6", 55));
        await Assert.ThrowsAsync<ValueAlreadyUsedException>(() => unitOfWork.SaveChangesAsync());
    }

    [Fact]
    public async Task AttemptFilterByName_IgnoresCaseAndTellsAccentsApart_AndNamesAreInSpanishOrder()
    {
        await using (var seed = _db.CreateContext())
        {
            foreach (var name in new[] { "zoe", "Íñigo", "INIGO", "inigo", "Ñandú", "Nuria", "Óscar" })
            {
                await seed.Database.ExecuteSqlAsync($"""
                    INSERT INTO "AllocationAttempts" ("AttemptedAt", "AttemptedBy", "TripId", "DriverId", "VehicleId", "BaseId", "Outcome", "RuleIds", "CreatedAt")
                    VALUES ('2043-01-01Z', {name}, 1, 1, 1, 1, 'Assigned', ARRAY['R1', 'R2'], '2043-01-01Z')
                    """);
            }
        }

        await using var context = _db.CreateContext();
        var audit = new AllocationAttemptAudit(context);
        var filter = new AttemptAuditFilter(new DateTime(2043, 1, 1), new DateTime(2043, 1, 2), AttemptedBy: "Inigo", RuleId: "R2");

        var page = await audit.SearchAsync(filter, 1, 50);

        Assert.Equal(2, page.Page.TotalCount);
        Assert.Equal(["inigo", "íñigo", "nuria", "ñandú", "óscar", "zoe"], page.People.Select(name => name.ToLowerInvariant()));
    }

    [Fact]
    public async Task Lock_IsNotSharedByResourcesWithTheSameNumber()
    {
        await using (await _db.HoldAsync("lof:trip:7", "lof:vehicle:7", "lof:driver:8", "lof:driver:-7"))
        {
            Assert.True(await LockAsync(ResourceClaim.Driver(7), waitMilliseconds: 0));
        }
    }

    /// <summary>The driver is freed when two thirds of the wait are gone: the vehicle only has the last one.</summary>
    [Fact]
    public async Task Lock_WaitIsForAllTheResourcesTogether()
    {
        await using var vehicle = await _db.HoldAsync("lof:vehicle:2");
        var driver = await _db.HoldAsync("lof:driver:1");
        var clock = Stopwatch.StartNew();

        var waiting = LockAsync(ResourceClaim.Allocation(3, 1, 2), waitMilliseconds: 1_500);
        await Task.Delay(1_000);
        Assert.False(waiting.IsCompleted);
        await driver.DisposeAsync();
        var busy = await Assert.ThrowsAsync<ResourceBusyException>(() => waiting);

        Assert.Equal("lof:vehicle:2", busy.Resource);
        Assert.InRange(clock.ElapsedMilliseconds, 1_300, 2_300);
    }

    /// <summary>What a pooler in transaction mode asks for: nothing is left in the connection, which the next one gets.</summary>
    [Fact]
    public async Task Lock_LeavesNothingInTheConnection()
    {
        await using var context = _db.CreateContext();
        await context.Database.OpenConnectionAsync();
        using var unitOfWork = new UnitOfWork(context, lockSettings: new ResourceLockSettings(5_000));
        var driver = new Driver("X", "Ana", "Pérez");

        var (heldInside, limitInside) = await unitOfWork.ExecuteExclusiveAsync(
            ResourceClaim.Allocation(1, 2, 3),
            async _ => (await AdvisoryLocksAsync(context), await LockTimeoutAsync(context)),
            _ => driver);

        Assert.Equal(3, heldInside);
        Assert.Equal("0", limitInside);
        Assert.Equal(0, await AdvisoryLocksAsync(context));
        Assert.Equal("0", await LockTimeoutAsync(context));
    }

    [Fact]
    public async Task NullCharacter_IsABadRequest()
    {
        using var api = new ApiInstance(_db.ConnectionString);
        using var body = new StringContent("""{ "Name": "Ba\u0000se" }""", Encoding.UTF8, "application/json");

        using var written = await api.Client.PostAsync("/api/bases", body);
        using var asked = await api.Client.GetAsync("/api/engine/attempts?by=a%00b");

        Assert.Equal(HttpStatusCode.BadRequest, written.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, asked.StatusCode);
    }

    private async Task<bool> LockAsync(ResourceClaim claim, int waitMilliseconds)
    {
        await using var context = _db.CreateContext();
        using var unitOfWork = new UnitOfWork(context, lockSettings: new ResourceLockSettings(waitMilliseconds));
        var driver = new Driver("X", "Ana", "Pérez");
        return await unitOfWork.ExecuteExclusiveAsync(claim, _ => Task.FromResult(true), _ => driver);
    }

    private static Task<int> AdvisoryLocksAsync(DispatchGuardDbContext context) =>
        context.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid()").SingleAsync();

    private static Task<string> LockTimeoutAsync(DispatchGuardDbContext context) =>
        context.Database.SqlQueryRaw<string>("SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync();

    private async Task<int> SaveTripAsync(DateTime start)
    {
        await using var context = _db.CreateContext();
        var (origin, destination) = (Site("Origen"), Site("Destino"));
        var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(1)), 10, ServiceType.Regular);
        context.AddRange(origin, destination, trip);
        await context.SaveChangesAsync();
        return trip.Id;
    }

    private static Location Site(string name) => new(name, name, new GeoCoordinate(40.4m, -3.7m), "Madrid", "Madrid");

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
