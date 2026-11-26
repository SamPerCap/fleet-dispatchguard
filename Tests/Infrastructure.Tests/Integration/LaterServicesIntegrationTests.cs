using System.Net;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// An allocation that would make a later, already assigned service illegal is rejected; forcing it marks the later one and the
/// mark is released with the service that caused it (SPEC 0063). Every test works on its own week so that only its own trips count.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Regulation", "Arts. 6 to 8")]
public sealed class LaterServicesIntegrationTests : IDisposable
{
    private const string Reason = "Único conductor disponible para el grupo";

    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public LaterServicesIntegrationTests(PostgreSqlFixture db)
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

    [Fact]
    public async Task Allocate_TripThatMakesALaterOneIllegal_Returns422WithTheLaterServices()
    {
        var seed = await SeedAsync(new DateTime(2037, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var response = await AllocateAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.True(body.GetProperty("forcible").GetBoolean());
        var later = Assert.Single(body.GetProperty("laterServices").EnumerateArray());
        Assert.Equal(seed.Later, later.GetProperty("TripId").GetInt32());
        Assert.Equal("Art. 7", later.GetProperty("Article").GetString());
        Assert.Contains($"Servicio #{seed.Later} (05/01 13:00), Art. 7", later.GetProperty("Message").GetString()); // 12:00 UTC, Madrid
        Assert.Contains("6 h con este servicio, 4 h sin él (máximo 4,5 h)", later.GetProperty("Message").GetString());
        var item = Assert.Single(body.GetProperty("explanation").GetProperty("Business").EnumerateArray(),
            i => i.GetProperty("Title").GetString() == "Servicios posteriores ya asignados");
        Assert.Equal("Failed", Outcome(item));

        await using var read = _db.CreateContext();
        Assert.Null((await read.Trips.AsNoTracking().SingleAsync(t => t.Id == seed.Earlier)).AssignedDriverId);
        var attempt = await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == seed.Earlier);
        Assert.Equal(AllocationOutcome.RejectedCompliance, attempt.Outcome);
        Assert.Equal(["later-services", "continuous"], attempt.RuleIds);
    }

    [Fact]
    public async Task Allocate_LegalTrip_ExplainsHowManyLaterServicesWereEvaluated()
    {
        var seed = await SeedAsync(new DateTime(2037, 1, 19, 0, 0, 0, DateTimeKind.Utc), earlierStartHour: 7); // ends 09:00: 3 h before the later one

        var body = await JsonAsync(await AllocateAsync(seed.Earlier, seed.Driver, seed.Vehicle, HttpStatusCode.OK));

        var item = Assert.Single(body.GetProperty("Explanation").GetProperty("Business").EnumerateArray(),
            i => i.GetProperty("Title").GetString() == "Servicios posteriores ya asignados");
        Assert.Equal("Passed", Outcome(item));
        Assert.Contains("1 servicio posterior evaluado hasta el 02/02 11:00", item.GetProperty("Detail").GetString());
    }

    [Fact]
    public async Task DriverOptions_DiscardTheDriverWithTheLaterServiceAsReason()
    {
        var seed = await SeedAsync(new DateTime(2037, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        var body = await JsonAsync(await _client.GetAsync($"/api/trips/{seed.Earlier}/driver-options"));

        Assert.DoesNotContain(body.GetProperty("Candidates").EnumerateArray(), c => c.GetProperty("DriverId").GetInt32() == seed.Driver);
        var discarded = Assert.Single(body.GetProperty("Discarded").EnumerateArray(), d => d.GetProperty("DriverId").GetInt32() == seed.Driver);
        Assert.Contains($"Servicio #{seed.Later}", discarded.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task Force_MarksTheLaterServiceAndRecordsASingleAttempt()
    {
        var seed = await SeedAsync(new DateTime(2037, 2, 16, 0, 0, 0, DateTimeKind.Utc));

        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle, "Lucía");

        var later = await TripAsync(seed.Later);
        Assert.True(later.IsForced);
        Assert.Null(later.ForcedReason);
        Assert.Equal(new ForcedByTrip(seed.Earlier, Reason, "Lucía"), Assert.Single(later.ForcedByTrips));
        Assert.Equal($"Forzado por el servicio #{seed.Earlier} (lo decidió Lucía): {Reason}", later.ForcedDescription.Es());
        Assert.Equal(TripStatus.Assigned, later.Status);
        Assert.False(later.LegalValidation!.IsCompliant);
        Assert.Contains(later.LegalValidation.Violations, v => v.Es().Contains("6 h"));
        Assert.True((await TripAsync(seed.Earlier)).IsForced);

        await using var read = _db.CreateContext();
        var attempt = Assert.Single(await read.AllocationAttempts.AsNoTracking().Where(a => a.TripId == seed.Earlier || a.TripId == seed.Later).ToListAsync());
        Assert.Equal((AllocationOutcome.Forced, seed.Earlier), (attempt.Outcome, attempt.TripId));
        Assert.Contains($"Servicios posteriores afectados: #{seed.Later}", attempt.Detail);
        Assert.Equal(["later-services", "continuous"], attempt.RuleIds);

        // The API shows the mark wherever the trip is read.
        var dto = await JsonAsync(await _client.GetAsync($"/api/trips/{seed.Later}"));
        Assert.True(dto.GetProperty("IsForced").GetBoolean());
        Assert.Contains($"#{seed.Earlier}", dto.GetProperty("ForcedDescription").GetString());
    }

    [Fact]
    public async Task CancelSource_ReleasesTheMarkAndRevalidatesTheLater()
    {
        var seed = await SeedAsync(new DateTime(2037, 3, 2, 0, 0, 0, DateTimeKind.Utc));
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/trips/{seed.Earlier}/cancel", null)).StatusCode);

        await AssertReleasedAsync(seed.Later);
    }

    [Fact]
    public async Task CancelSourceBooking_ReleasesTheMark()
    {
        var seed = await SeedAsync(new DateTime(2037, 3, 16, 0, 0, 0, DateTimeKind.Utc));
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);
        var bookingId = (await TripAsync(seed.Earlier)).BookingId;

        Assert.True((await _client.DeleteAsync($"/api/bookings/{bookingId}")).IsSuccessStatusCode);

        await AssertReleasedAsync(seed.Later);
    }

    [Fact]
    public async Task ReassignSourceToAnotherDriver_ReleasesTheMark()
    {
        var seed = await SeedAsync(new DateTime(2037, 4, 13, 0, 0, 0, DateTimeKind.Utc));
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        await AllocateAsync(seed.Earlier, seed.OtherDriver, seed.Vehicle, HttpStatusCode.OK);

        await AssertReleasedAsync(seed.Later);
        Assert.False((await TripAsync(seed.Earlier)).IsForced);
    }

    [Fact]
    public async Task ReassignSourceToTheSameDriver_KeepsBothMarks()
    {
        var seed = await SeedAsync(new DateTime(2037, 4, 27, 0, 0, 0, DateTimeKind.Utc));
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        await AllocateAsync(seed.Earlier, seed.Driver, seed.OtherVehicle, HttpStatusCode.OK); // Only the vehicle changes (SPEC 0063 R4).

        var earlier = await TripAsync(seed.Earlier);
        Assert.Equal(seed.OtherVehicle, earlier.AssignedVehicleId);
        Assert.Equal(Reason, earlier.ForcedReason);
        Assert.Equal(seed.Earlier, Assert.Single((await TripAsync(seed.Later)).ForcedByTrips).SourceTripId);
    }

    [Fact]
    public async Task LaterMarkedByTwoSources_ReleasingOneKeepsTheOther()
    {
        var seed = await SeedAsync(new DateTime(2037, 5, 11, 0, 0, 0, DateTimeKind.Utc),
            earlierStartHour: 11.4, earlierHours: 0.6, second: true); // each one leaves less than 45 min before the later service
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);
        await ForceAsync(seed.Second, seed.Driver, seed.Vehicle);
        Assert.Equal(new[] { seed.Earlier, seed.Second }.Order(), (await TripAsync(seed.Later)).ForcedByTrips.Select(f => f.SourceTripId).Order());

        await _client.PostAsync($"/api/trips/{seed.Earlier}/cancel", null);

        var later = await TripAsync(seed.Later);
        Assert.Equal(seed.Second, Assert.Single(later.ForcedByTrips).SourceTripId);
        Assert.True(later.IsForced);
        Assert.False(later.LegalValidation!.IsCompliant); // revalidated: the second one still makes it illegal
    }

    [Fact]
    public async Task LaterForcedByItself_KeepsItsOwnMarkWhenTheSourceIsReleased()
    {
        var seed = await SeedAsync(new DateTime(2037, 5, 25, 0, 0, 0, DateTimeKind.Utc));
        await using (var write = _db.CreateContext())
        {
            var own = await write.Trips.Include(t => t.AssignedDriver).Include(t => t.AssignedVehicle).SingleAsync(t => t.Id == seed.Later);
            own.ForceAssign(own.AssignedDriver!, own.AssignedVehicle!, LegalValidation.NonCompliant(Texts.AsWritten("x")), AllocationOverride.Create("Motivo propio del servicio", "Marta"));
            await write.SaveChangesAsync();
        }

        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);
        var both = await TripAsync(seed.Later);
        Assert.Equal(("Motivo propio del servicio", "Marta"), (both.ForcedReason, both.ForcedBy));
        Assert.Single(both.ForcedByTrips);

        await _client.PostAsync($"/api/trips/{seed.Earlier}/cancel", null);

        var after = await TripAsync(seed.Later);
        Assert.Empty(after.ForcedByTrips);
        Assert.Equal("Motivo propio del servicio", after.ForcedReason);
        Assert.True(after.IsForced);
    }

    [Fact]
    public async Task LaterReassignedBeforeTheSource_LosesTheMarkAndStaysClean()
    {
        var seed = await SeedAsync(new DateTime(2037, 6, 8, 0, 0, 0, DateTimeKind.Utc));
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        await AllocateAsync(seed.Later, seed.OtherDriver, seed.Vehicle, HttpStatusCode.OK);
        Assert.False((await TripAsync(seed.Later)).IsForced);

        await _client.PostAsync($"/api/trips/{seed.Earlier}/cancel", null);
        var later = await TripAsync(seed.Later);
        Assert.False(later.IsForced);
        Assert.Equal(seed.OtherDriver, later.AssignedDriverId);
    }

    [Fact]
    public async Task ForceAgainWithAnotherReason_RenewsTheReasonOfTheLater()
    {
        // The earlier trip is illegal by itself (5 h non-stop), so it has to be forced every time.
        var seed = await SeedAsync(new DateTime(2037, 6, 22, 0, 0, 0, DateTimeKind.Utc), earlierStartHour: 7, earlierHours: 5);
        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle);

        await ForceAsync(seed.Earlier, seed.Driver, seed.OtherVehicle, "Pablo", "Otro motivo distinto");

        var mark = Assert.Single((await TripAsync(seed.Later)).ForcedByTrips);
        Assert.Equal(("Otro motivo distinto", "Pablo"), (mark.Reason, mark.By));
    }

    [Fact]
    public async Task ExistingRows_WithNullMarks_LoadWithoutAny()
    {
        var seed = await SeedAsync(new DateTime(2037, 7, 6, 0, 0, 0, DateTimeKind.Utc));
        await using (var write = _db.CreateContext())
            await write.Database.ExecuteSqlAsync($"UPDATE \"Trips\" SET \"ForcedByTrips\" = NULL WHERE \"Id\" = {seed.Later}");

        var later = await TripAsync(seed.Later);
        Assert.Empty(later.ForcedByTrips);
        Assert.False(later.IsForced);

        await ForceAsync(seed.Earlier, seed.Driver, seed.Vehicle); // and it can be marked
        Assert.Single((await TripAsync(seed.Later)).ForcedByTrips);
    }

    private sealed record Seed(int Driver, int OtherDriver, int Vehicle, int OtherVehicle, int Earlier, int Later, int Second);

    /// <summary>
    /// A driver with verifiable history and a 12:00–16:00 service already assigned (4 h). The pending one ends at 12:00 by
    /// default (2 h): assigning it leaves the later at 6 h of continuous driving. <paramref name="second"/>: another pending
    /// one of 1 h that ends right before the first.
    /// </summary>
    private async Task<Seed> SeedAsync(DateTime day, double earlierStartHour = 10, double earlierHours = 2, bool second = false)
    {
        await using var write = _db.CreateContext();
        var (driver, other) = (TripRepositoryTests.NewDriver(), TripRepositoryTests.NewDriver());
        var (vehicle, otherVehicle) = (TripRepositoryTests.NewVehicle(), TripRepositoryTests.NewVehicle());
        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        write.AddRange(driver, other, vehicle, otherVehicle, origin, destination);
        await write.SaveChangesAsync();

        Trip NewTrip(DateTime start, double hours) =>
            new(origin, destination, new TimeWindow(start, start.AddMinutes(Math.Round(hours * 60))), requiredSeats: 10, ServiceType.Regular);

        foreach (var eachDriver in new[] { driver, other })
        {
            var past = NewTrip(day.AddDays(-9).AddHours(8), 2);
            past.AssignResources(eachDriver, vehicle);
            past.Complete();
            write.Trips.Add(past);
        }

        var earlier = NewTrip(day.AddMinutes(Math.Round(earlierStartHour * 60)), earlierHours);
        var later = NewTrip(day.AddHours(12), 4);
        later.AssignResources(driver, vehicle);
        later.SetLegalValidation(LegalValidation.Compliant());
        var extra = second ? NewTrip(day.AddMinutes(Math.Round((earlierStartHour - 1) * 60)), 1) : null;
        write.Trips.AddRange(earlier, later);
        if (extra is not null) write.Trips.Add(extra);
        await write.SaveChangesAsync();
        return new Seed(driver.Id, other.Id, vehicle.Id, otherVehicle.Id, earlier.Id, later.Id, extra?.Id ?? 0);
    }

    private async Task<HttpResponseMessage> AllocateAsync(int tripId, int driverId, int vehicleId, HttpStatusCode? expected = null)
    {
        var response = await _client.AllocateAsync(tripId, driverId, vehicleId, "test");
        if (expected is { } status) Assert.True(status == response.StatusCode, await response.Content.ReadAsStringAsync());
        return response;
    }

    private async Task ForceAsync(int tripId, int driverId, int vehicleId, string by = "Lucía", string reason = Reason)
    {
        var response = await _client.AllocateAsync(tripId, driverId, vehicleId, by, force: true, reason: reason);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("Forced").GetBoolean(), body);
    }

    private async Task<Trip> TripAsync(int id)
    {
        await using var read = _db.CreateContext();
        return await read.Trips.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task AssertReleasedAsync(int laterId)
    {
        var later = await TripAsync(laterId);
        Assert.False(later.IsForced);
        Assert.Null(later.ForcedDescription);
        Assert.True(later.LegalValidation!.IsCompliant);
        Assert.Equal(TripStatus.Assigned, later.Status);
    }

    private static string Outcome(JsonElement item)
    {
        var outcome = item.GetProperty("Outcome");
        return outcome.ValueKind == JsonValueKind.Number ? ((ExplanationOutcome)outcome.GetInt32()).ToString() : outcome.GetString()!;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
