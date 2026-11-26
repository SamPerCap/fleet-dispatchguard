using System.Diagnostics;
using System.Net;
using Xunit.Abstractions;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// The figure on the cover (SPEC 0096): bursts of simultaneous assignments that cannot all be legal, split over two API
/// instances on one real PostgreSQL. Each burst gives one driver eight services at the same hour, each with its own bus, so
/// exactly one can be accepted.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrentBurstTests(PostgreSqlFixture server, ITestOutputHelper output)
{
    private const int Bursts = 60;
    private const int RequestsPerBurst = 8;

    [Fact]
    public async Task SixtyBurstsOfEightClashingAssignments_AcceptOnePerBurst()
    {
        var db = new ConcurrencyDatabase(server);
        await db.InitializeAsync();
        try
        {
            using var oneInstance = new ApiInstance(db.ConnectionString);
            using var otherInstance = new ApiInstance(db.ConnectionString);
            var day = new DateTime(2042, 2, 4, 0, 0, 0, DateTimeKind.Utc);
            var fleet = await ConcurrentAllocationTests.FleetAsync(db, day, drivers: Bursts, vehicles: Bursts * RequestsPerBurst);
            var trips = new int[Bursts * RequestsPerBurst];
            for (var n = 0; n < trips.Length; n++)
            {
                trips[n] = await ConcurrentAllocationTests.TripAsync(db, day, fleet, startHour: 9, hours: 2);
            }

            var accepted = 0;
            var rejectedAsOverlap = 0;
            var unexpected = new List<string>();
            var clock = Stopwatch.StartNew();
            for (var burst = 0; burst < Bursts; burst++)
            {
                var driver = fleet.Drivers[burst];
                var answers = await Task.WhenAll(Enumerable.Range(burst * RequestsPerBurst, RequestsPerBurst).Select(n => Task.Run(() =>
                    ConcurrentAllocationTests.Allocate((n % 2 == 0 ? oneInstance : otherInstance).Client, trips[n], driver, fleet.Vehicles[n]))));
                var acceptedNow = answers.Count(a => a.StatusCode == HttpStatusCode.OK);
                accepted += acceptedNow;
                rejectedAsOverlap += answers.Count(a => a.StatusCode == HttpStatusCode.Conflict);
                unexpected.AddRange(answers.Where(a => a.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Conflict)).Select(a => $"burst {burst}: {(int)a.StatusCode}"));
                if (acceptedNow != 1) unexpected.Add($"burst {burst}: {acceptedNow} accepted");
                foreach (var answer in answers) answer.Dispose();
            }

            output.WriteLine($"{Bursts} bursts of {RequestsPerBurst}: {accepted} accepted, {rejectedAsOverlap} rejected as overlap, {clock.Elapsed.TotalSeconds:F1} s");
            Assert.Empty(unexpected);
            Assert.Equal(Bursts, accepted);
            Assert.Equal(Bursts * (RequestsPerBurst - 1), rejectedAsOverlap);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }
}
