using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Random operations over few drivers, vehicles and services, from several clients on two API instances (SPEC 0065). What
/// happens depends on the scheduler; what must hold at the end does not.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrentLoadTests(PostgreSqlFixture server, ITestOutputHelper output)
{
    private const int Clients = 8;
    private const int OperationsPerClient = 40;

    private static readonly HttpStatusCode[] Expected =
    [
        HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity
    ];

    [Fact]
    public async Task RandomOperationsAtOnce_KeepEveryRule()
    {
        var db = new ConcurrencyDatabase(server);
        await db.InitializeAsync();
        try
        {
            using var oneInstance = new ApiInstance(db.ConnectionString);
            using var otherInstance = new ApiInstance(db.ConnectionString);
            var day = new DateTime(2041, 3, 4, 0, 0, 0, DateTimeKind.Utc);
            var fleet = await ConcurrentAllocationTests.FleetAsync(db, day, drivers: 3, vehicles: 3);
            var trips = new List<int>();
            for (var n = 0; n < 30; n++) // three days, 3 h services every 75 minutes: neighbours overlap, and four in a day are 12 h
            {
                trips.Add(await ConcurrentAllocationTests.TripAsync(db, day.AddDays(n / 10), fleet, 5 + (n % 10 * 1.25), 3));
            }

            var statuses = new ConcurrentDictionary<HttpStatusCode, int>();
            var unexpected = new ConcurrentBag<string>();
            await Task.WhenAll(Enumerable.Range(0, Clients).Select(n => Task.Run(() => RunAsync(n, (n % 2 == 0 ? oneInstance : otherInstance).Client))));

            output.WriteLine(string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => $"{(int)s.Key}: {s.Value}")));
            Assert.Empty(unexpected);
            await EveryRuleHoldsAsync(db);

            async Task RunAsync(int seed, HttpClient client)
            {
                var random = new Random(seed);
                for (var n = 0; n < OperationsPerClient; n++)
                {
                    var (trip, driver, vehicle) = (Pick(trips), Pick(fleet.Drivers), Pick(fleet.Vehicles));
                    using var response = random.Next(100) switch
                    {
                        < 60 => await ConcurrentAllocationTests.Allocate(client, trip, driver, vehicle),
                        < 85 => await ConcurrentAllocationTests.Allocate(client, trip, driver, vehicle, force: true),
                        < 90 => await client.PostAsync($"/api/trips/{trip}/cancel", null),
                        // A leave rejects, so it only takes an hour: whole days would leave nothing to compete for.
                        < 95 => await client.PostAsJsonAsync($"/api/drivers/{driver}/unavailabilities", AnHourOff()),
                        _ => await client.PostAsJsonAsync($"/api/vehicles/{vehicle}/unavailabilities", new { Start = day.AddDays(9), End = day.AddDays(10), Type = "Repair" })
                    };
                    statuses.AddOrUpdate(response.StatusCode, 1, (_, count) => count + 1);
                    if (!Expected.Contains(response.StatusCode))
                    {
                        unexpected.Add($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                    }
                }

                int Pick(IReadOnlyList<int> ids) => ids[random.Next(ids.Count)];

                object AnHourOff()
                {
                    var start = day.AddDays(random.Next(3)).AddHours(random.Next(5, 20));
                    return new { Start = start, End = start.AddHours(1), Type = "SickLeave" };
                }
            }
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    private static async Task EveryRuleHoldsAsync(ConcurrencyDatabase db)
    {
        await ConcurrentAllocationTests.NoOrphanMarksAsync(db);

        await using var read = db.CreateContext();
        var trips = await read.Trips.AsNoTracking().Include(t => t.AssignedDriver).ToListAsync();

        // A cancelled service never comes back.
        Assert.All(trips, t => Assert.Equal(t.Status != TripStatus.Cancelled, t.IsActive));

        var assigned = trips.Where(t => t is { IsActive: true, Status: TripStatus.Assigned }).ToList();
        Assert.NotEmpty(assigned);
        foreach (var (one, other) in assigned.SelectMany(x => assigned.Where(y => y.Id > x.Id), (x, y) => (x, y)))
        {
            Assert.False(one.ScheduledWindow.OverlapsWith(other.ScheduledWindow) && one.AssignedDriverId == other.AssignedDriverId, $"Driver of #{one.Id} and #{other.Id}");
            Assert.False(one.ScheduledWindow.OverlapsWith(other.ScheduledWindow) && one.AssignedVehicleId == other.AssignedVehicleId, $"Vehicle of #{one.Id} and #{other.Id}");
        }

        // Nothing illegal that nobody decided to force.
        var legal = new LegalValidationService(new DrivingRecordRepository(read));
        foreach (var trip in assigned.Where(t => !t.IsForced))
        {
            var result = await legal.ValidateDriverForTrip(trip.AssignedDriver!, trip);
            Assert.True(result.IsCompliant, $"#{trip.Id} is illegal and nobody forced it: {string.Join("; ", result.Violations)}");
        }

        // Every attempt that succeeded left its record (the log is written after the commit).
        Assert.True(await read.AllocationAttempts.AnyAsync(x => x.Outcome == AllocationOutcome.Assigned));
    }
}
