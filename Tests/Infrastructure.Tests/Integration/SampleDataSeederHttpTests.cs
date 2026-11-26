using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.DemoData;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Uses its own database in the shared container: the demo's many active drivers would otherwise change the candidate lists of
/// other tests.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SampleDataSeederHttpTests(PostgreSqlFixture db)
{
    private static readonly DateTime Now = new(2040, 1, 6, 9, 0, 0, DateTimeKind.Utc); // Friday 10:00 Madrid: the demo reaches the two-week alert from Friday on (SPEC 0064).

    [Fact]
    public async Task Seed_TwiceOnEmptyDb_AllocatesLegallyOnce_AndRaisesEveryAlertInEveryBase()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "DispatchGuardSeed" }.ConnectionString;
        await using (var ctx = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(connectionString).Options))
            await ctx.Database.MigrateAsync();

        await using var factory = new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString);
        });
        using var client = factory.CreateClient();
        var plan = DemoPlan.Build(Now);

        // Every planned allocation passed the API's legal checks; every planned wrong attempt was rejected.
        var first = await new ApiSeeder(client).SeedAsync(Now);
        Assert.Equal(2, first.BasesCreated);
        Assert.Equal(plan.Trips.Count, first.TripsCreated);
        Assert.Equal(plan.Trips.Count(t => t.Covered), first.TripsAllocated);
        Assert.Equal(plan.Rejections.Count, first.AttemptsRejected);
        Assert.Equal(plan.DriverLeaves.Count + plan.VehicleLeaves.Count, first.LeavesCreated); // 12, with the past vacation and inspection.

        // Idempotent.
        Assert.Equal(new SeedReport(0, 0, 0, 0, 0, 0, 0, 0, 0), await new ApiSeeder(client).SeedAsync(Now));

        var bases = await client.GetFromJsonAsync<List<BaseDto>>("/api/bases");
        Assert.Equal(DemoDataset.Bases.Select(b => b.Name).Order(), bases!.Select(b => b.Name).Order());

        // Every alert type in every base, at the plan's "now".
        using var scope = factory.Services.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<IControlCenterService>();
        foreach (var operatingBase in bases!)
        {
            var alerts = await control.AlertsAsync(operatingBase.Id, Now, Now, 1, ControlCenterService.MaxPageSize);
            var types = alerts.Items.Select(a => a.Type).ToHashSet();
            // Only a person forces an assignment (SPEC 0056) or lowers the seats over an assigned service (SPEC 0079).
            ControlAlertType[] neverInTheDemo = [ControlAlertType.ForcedAssignment, ControlAlertType.InsufficientSeats];
            Assert.All(Enum.GetValues<ControlAlertType>().Except(neverInTheDemo), t => Assert.Contains(t, types));
            Assert.Empty(types.Intersect(neverInTheDemo));
            Assert.Contains(alerts.Items, a => a.Type == ControlAlertType.UnassignedTrip && a.Severity == ControlAlertSeverity.Urgent);
            Assert.Contains(alerts.Items, a => a.Type == ControlAlertType.UnassignedTrip && a.Severity == ControlAlertSeverity.Warning);

            var metrics = await control.EngineMetricsAsync(operatingBase.Id, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            Assert.True(metrics.IllegalBlocked > 0 && metrics.Validations > metrics.IllegalBlocked * 5);
        }

        await AssertTheTourFindsACaseAsync(client);

        // Someone cancels the service of the driver now on leave and seeds again: it comes back, unassigned (SPEC 0066).
        var sick = plan.DriverLeaves.First(l => l.End > Now);
        var night = plan.Trips.Single(t => t.DriverId == sick.DriverId && t.Start >= sick.Start && t.Start < sick.End);
        int nightId;
        await using (var read = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(connectionString).Options))
            nightId = (await read.Trips.SingleAsync(t => t.Description == night.Description)).Id;
        Assert.True((await client.PostAsync($"/api/trips/{nightId}/cancel", null)).IsSuccessStatusCode);
        var again = await new ApiSeeder(client).SeedAsync(Now);
        Assert.Equal((1, 0, 0), (again.TripsCreated, again.TripsAllocated, again.AttemptsRejected));
    }

    /// <summary>
    /// What web/src/tourCase.ts does, with the codes it looks for (SPECs 0093 and 0095): somebody rejected for the regulation in a
    /// service that has not departed is still rejected for it.
    /// </summary>
    private static async Task AssertTheTourFindsACaseAsync(HttpClient client)
    {
        using var attempts = await client.GetFromJsonAsync<JsonDocument>("/api/engine/attempts?kind=Blocked&days=7&pageSize=100");
        var rejected = attempts!.RootElement.GetProperty("Items").EnumerateArray()
            .Where(attempt => attempt.GetProperty("Outcome").GetString() == "RejectedCompliance")
            .Select(attempt => (Trip: attempt.GetProperty("TripId").GetInt32(), Driver: attempt.GetProperty("DriverId").GetInt32()))
            .ToList();
        Assert.NotEmpty(rejected);

        var stillIllegal = 0;
        foreach (var (trip, driver) in rejected.Distinct())
        {
            using var tripData = await client.GetFromJsonAsync<JsonDocument>($"/api/trips/{trip}");
            using var options = await client.GetFromJsonAsync<JsonDocument>($"/api/trips/{trip}/driver-options");
            var departs = tripData!.RootElement.GetProperty("Start").GetDateTime();
            stillIllegal += options!.RootElement.GetProperty("Discarded").EnumerateArray().Count(discarded =>
                departs > Now
                && discarded.GetProperty("DriverId").GetInt32() == driver
                && discarded.GetProperty("Cause").GetString() == "NotCompliant"
                && !string.IsNullOrEmpty(discarded.GetProperty("Broken").GetString()));
        }

        Assert.True(stillIllegal > 0, $"None of the {rejected.Count} rejections of the demo is a case for the tour");
    }
}
