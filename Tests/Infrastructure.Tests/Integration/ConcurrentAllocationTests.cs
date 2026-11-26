using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Two API instances over real PostgreSQL (SPEC 0065). One request is stopped right before it writes (it has already read and
/// decided); the other one runs until it finishes or the server says that it waits; then the first goes on.
/// Without the locks the second always finishes first, so every test shows its anomaly every time.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrentAllocationTests : IClassFixture<ConcurrencyDatabase>, IDisposable
{
    private const string Reason = "Único conductor disponible para el grupo";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Each test has its own three weeks, so only its own services count.</summary>
    private static int _weeks;

    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _a;
    private readonly ApiInstance _b;
    private readonly DateTime _day = new DateTime(2041, 1, 7, 0, 0, 0, DateTimeKind.Utc).AddDays(21 * Interlocked.Increment(ref _weeks));

    public ConcurrentAllocationTests(ConcurrencyDatabase db)
    {
        _db = db;
        _a = new ApiInstance(db.ConnectionString);
        _b = new ApiInstance(db.ConnectionString);
    }

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
    }

    [Fact]
    public async Task SameDriver_OverlappingServices_OnlyOneIsAssigned()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 2);
        var (t1, t2) = (await TripAsync(fleet, 8, 2), await TripAsync(fleet, 9, 2));

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[0]), c => Allocate(c, t2, fleet.Drivers[0], fleet.Vehicles[1]));

        await ExpectAsync(HttpStatusCode.OK, first);
        var body = await ExpectAsync(HttpStatusCode.Conflict, second);
        Assert.Equal([t1], body.GetProperty("conflictingTripIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(1, await AssignedAsync(t1, t2));
        Assert.Equal([AllocationOutcome.RejectedSchedule], await OutcomesAsync(t2));
        Assert.True(waited);
    }

    [Fact]
    public async Task SameVehicle_OverlappingServices_OnlyOneIsAssigned()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 1);
        var (t1, t2) = (await TripAsync(fleet, 8, 2), await TripAsync(fleet, 9, 2));

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[0]), c => Allocate(c, t2, fleet.Drivers[1], fleet.Vehicles[0]));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.Conflict, second);
        Assert.Equal(1, await AssignedAsync(t1, t2));
        Assert.True(waited);
    }

    /// <summary>The second one leaves the service forced; the first, that read it as unassigned, must not be mixed with it.</summary>
    [Fact]
    public async Task SameService_NormalAndForced_TheLastOneIsWholeAndLeavesNoMarks()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var service = await TripAsync(fleet, 10, 2);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, service, fleet.Drivers[0], fleet.Vehicles[0], force: true), c => Allocate(c, service, fleet.Drivers[1], fleet.Vehicles[1]));

        Assert.True((await ExpectAsync(HttpStatusCode.OK, first)).GetProperty("Forced").GetBoolean());
        await ExpectAsync(HttpStatusCode.OK, second);
        var final = await ReadTripAsync(service);
        Assert.Equal(fleet.Drivers[1], final.AssignedDriverId);
        Assert.False(final.IsForced);
        Assert.Empty((await ReadTripAsync(later)).ForcedByTrips);
        Assert.True((await ReadTripAsync(later)).LegalValidation!.IsCompliant);
        Assert.True(waited);
    }

    [Fact]
    public async Task SameService_ForcedWhileANormalOneWaits_IsNotLeftWithTheForcedMark()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var service = await TripAsync(fleet, 10, 2);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, service, fleet.Drivers[1], fleet.Vehicles[1]), c => Allocate(c, service, fleet.Drivers[0], fleet.Vehicles[0], force: true));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
        var final = await ReadTripAsync(service);
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), (final.AssignedDriverId, final.AssignedVehicleId));
        Assert.Equal(Reason, final.ForcedReason);
        Assert.Equal(service, Assert.Single((await ReadTripAsync(later)).ForcedByTrips).SourceTripId);
        Assert.True(waited);
    }

    [Fact]
    public async Task SameDriver_DailyDrivingOnlyBrokenByBoth_TheSecondIsRejectedByCompliance()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 3);
        await TripAsync(fleet, 5, 4, driver: 0, vehicle: 0);
        var (t1, t2) = (await TripAsync(fleet, 10, 4), await TripAsync(fleet, 15, 4)); // 4 + 4 = 8 h; with both, 12 h

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[1]), c => Allocate(c, t2, fleet.Drivers[0], fleet.Vehicles[2]));

        await ExpectAsync(HttpStatusCode.OK, first);
        var body = await ExpectAsync(HttpStatusCode.UnprocessableEntity, second);
        Assert.True(body.GetProperty("forcible").GetBoolean());
        Assert.Contains(body.GetProperty("violations").EnumerateArray(), v => v.ToString().Contains("12 h", StringComparison.Ordinal));
        Assert.Equal(1, await AssignedAsync(t1, t2));
        Assert.Equal([AllocationOutcome.RejectedCompliance], await OutcomesAsync(t2));
        Assert.True(waited);
    }

    /// <summary>The earlier one is legal alone; the later, saved meanwhile, would stop being legal (SPEC 0063).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameDriver_EarlierAndLaterServiceAtOnce_TheSecondSeesTheFirst(bool laterFirst)
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 2);
        var (earlier, later) = (await TripAsync(fleet, 10, 2), await TripAsync(fleet, 12, 4)); // 6 h without a break
        var (t1, t2) = laterFirst ? (later, earlier) : (earlier, later);

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[0]), c => Allocate(c, t2, fleet.Drivers[0], fleet.Vehicles[1]));

        await ExpectAsync(HttpStatusCode.OK, first);
        var body = await ExpectAsync(HttpStatusCode.UnprocessableEntity, second);
        Assert.Equal(laterFirst ? 1 : 0, body.GetProperty("laterServices").GetArrayLength());
        Assert.Equal(1, await AssignedAsync(t1, t2));
        Assert.True(waited);
    }

    [Fact]
    public async Task TwoServicesForcedAtOnce_TheLaterOneKeepsBothMarks()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 3);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var (t1, t2) = (await TripAsync(fleet, 9, 1), await TripAsync(fleet, 10, 2));

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[1], force: true), c => Allocate(c, t2, fleet.Drivers[0], fleet.Vehicles[2], force: true));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
        var marks = (await ReadTripAsync(later)).ForcedByTrips.Select(m => m.SourceTripId).ToList();
        Assert.Contains(t2, marks);
        Assert.Equal((await ReadTripAsync(t1)).IsForced, marks.Contains(t1) || (await ReadTripAsync(t1)).ForcedReason is not null);
        await NoOrphanMarksAsync();
        Assert.True(waited);
    }

    [Fact]
    public async Task CancelReleasesAMarkWhileAnotherServiceAddsOne_NoneIsLost()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 3);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var source = await TripAsync(fleet, 10, 2);
        var other = await TripAsync(fleet, 8, 2);
        await ExpectAsync(HttpStatusCode.OK, await Allocate(_a.Client, source, fleet.Drivers[0], fleet.Vehicles[1], force: true));

        var (first, second, waited) = await CrossAsync(
            c => c.PostAsync($"/api/trips/{source}/cancel", null), c => Allocate(c, other, fleet.Drivers[0], fleet.Vehicles[2], force: true));

        await ExpectAsync(HttpStatusCode.OK, first);
        // With the source cancelled, 8–10 and 12–16 have two hours between them: the other one is legal, nothing to force.
        Assert.False((await ExpectAsync(HttpStatusCode.OK, second)).GetProperty("Forced").GetBoolean());
        var final = await ReadTripAsync(later);
        Assert.Empty(final.ForcedByTrips);
        Assert.True(final.LegalValidation!.IsCompliant);
        Assert.True(waited);
    }

    [Fact]
    public async Task CancelWhileBeingForced_LeavesNoMarks()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 2);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var source = await TripAsync(fleet, 10, 2);

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, source, fleet.Drivers[0], fleet.Vehicles[1], force: true), c => c.PostAsync($"/api/trips/{source}/cancel", null));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
        var final = await ReadTripAsync(source);
        Assert.Equal((TripStatus.Cancelled, false), (final.Status, final.IsActive));
        Assert.Empty((await ReadTripAsync(later)).ForcedByTrips);
        Assert.True((await ReadTripAsync(later)).LegalValidation!.IsCompliant);
        Assert.True(waited);
    }

    [Fact]
    public async Task CancelWhileAssigning_StaysCancelled()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]), c => c.PostAsync($"/api/trips/{trip}/cancel", null));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
        var final = await ReadTripAsync(trip);
        Assert.Equal((TripStatus.Cancelled, false), (final.Status, final.IsActive));
        Assert.True(waited);
    }

    [Fact]
    public async Task AssignWhileCancelling_IsRejectedAsCancelled()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(
            c => c.PostAsync($"/api/trips/{trip}/cancel", null), c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]));

        await ExpectAsync(HttpStatusCode.OK, first);
        Assert.Contains("está cancelado", (await ExpectAsync(HttpStatusCode.Conflict, second)).GetProperty("detail").GetString());
        var final = await ReadTripAsync(trip);
        Assert.Equal((TripStatus.Cancelled, false), (final.Status, final.IsActive));
        Assert.Null(final.AssignedDriverId);
        Assert.True(waited);
    }

    [Fact]
    public async Task DeleteBookingWhileAssigningOneOfItsServices_AllStayCancelled()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);
        int booking;
        await using (var read = _db.CreateContext()) booking = (await read.Trips.SingleAsync(t => t.Id == trip)).BookingId;

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]), c => c.DeleteAsync($"/api/bookings/{booking}"));

        await ExpectAsync(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var final = await ReadTripAsync(trip);
        Assert.Equal((TripStatus.Cancelled, false), (final.Status, final.IsActive));
        Assert.True(waited);
    }

    [Fact]
    public async Task AssignWhileTheVehicleEntersTheWorkshop_IsRejected()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(c => Workshop(c, fleet.Vehicles[0]), c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]));

        await ExpectAsync(HttpStatusCode.OK, first);
        var body = await ExpectAsync(HttpStatusCode.UnprocessableEntity, second);
        Assert.Contains("taller", body.GetProperty("violations").ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await AssignedAsync(trip));
        Assert.True(waited);
    }

    /// <summary>A leave or a deactivation being saved at the same time never slips through, forced or not (SPEC 0066 R8).</summary>
    [Theory]
    [InlineData("leave", false)]
    [InlineData("leave", true)]
    [InlineData("delete-driver", false)]
    [InlineData("delete-driver", true)]
    public async Task AssignWhileTheDriverBecomesUnavailable_IsRejected(string kind, bool force)
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(
            c => kind == "leave" ? Leave(c, fleet.Drivers[0]) : c.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}"),
            c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0], force));

        Assert.True(first.IsSuccessStatusCode);
        var body = await ExpectAsync(HttpStatusCode.UnprocessableEntity, second);
        Assert.Contains(kind == "leave" ? "baja médica" : "no activo", body.GetProperty("violations")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(0, await AssignedAsync(trip));
        Assert.Equal([AllocationOutcome.RejectedDriver], await OutcomesAsync(trip));
        Assert.True(waited);
    }

    /// <summary>The leave or the workshop never gets in between the decision and the write of an assignment.</summary>
    [Theory]
    [InlineData("workshop")]
    [InlineData("leave")]
    [InlineData("delete-vehicle")]
    [InlineData("delete-driver")]
    public async Task UnavailabilityWhileAssigning_WaitsForTheAssignment(string kind)
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]), c => kind switch
        {
            "workshop" => Workshop(c, fleet.Vehicles[0]),
            "leave" => Leave(c, fleet.Drivers[0]),
            "delete-vehicle" => c.DeleteAsync($"/api/vehicles/{fleet.Vehicles[0]}"),
            _ => c.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}")
        });

        await ExpectAsync(HttpStatusCode.OK, first);
        Assert.True(second.IsSuccessStatusCode);
        Assert.True(waited);
    }

    [Theory]
    [InlineData("workshop")]
    [InlineData("leave")]
    [InlineData("delete-vehicle")]
    [InlineData("delete-driver")]
    [InlineData("end-workshop")]
    [InlineData("end-leave")]
    [InlineData("shorten-workshop")]
    [InlineData("shorten-leave")]
    public async Task AssignWhileAnUnavailabilityIsBeingSaved_Waits(string kind)
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);
        var leave = kind is "end-leave" or "shorten-leave" ? await UnavailabilityIdAsync(await Leave(_a.Client, fleet.Drivers[0])) : 0;
        var workshop = kind is "end-workshop" or "shorten-workshop" ? await UnavailabilityIdAsync(await Workshop(_a.Client, fleet.Vehicles[0])) : 0;

        var (first, second, waited) = await CrossAsync(c => kind switch
        {
            "workshop" => Workshop(c, fleet.Vehicles[0]),
            "leave" => Leave(c, fleet.Drivers[0]),
            "delete-vehicle" => c.DeleteAsync($"/api/vehicles/{fleet.Vehicles[0]}"),
            "delete-driver" => c.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}"),
            "end-workshop" => c.DeleteAsync($"/api/vehicles/{fleet.Vehicles[0]}/unavailabilities/{workshop}"),
            "shorten-workshop" => ShortenToDeparture(c, $"vehicles/{fleet.Vehicles[0]}", workshop, "Repair"),
            "shorten-leave" => ShortenToDeparture(c, $"drivers/{fleet.Drivers[0]}", leave, "SickLeave"),
            _ => c.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities/{leave}")
        }, c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]));

        Assert.True(first.IsSuccessStatusCode);
        // It decides with what the first one saved: an absence that began rejects, one that ended does not.
        var ended = kind.StartsWith("end-", StringComparison.Ordinal) || kind.StartsWith("shorten-", StringComparison.Ordinal);
        await ExpectAsync(ended ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, second);
        Assert.True(waited);
    }

    /// <summary>The service needs 10 seats; whoever comes second sees what the first one saved, and nothing assigned is taken away (SPEC 0079 R5).</summary>
    [Fact]
    public async Task AssignWhileTheSeatsAreLowered_IsRejected()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(c => Seats(c, fleet.Vehicles[0], 9), c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]));

        await ExpectAsync(HttpStatusCode.OK, first);
        var body = await ExpectAsync(HttpStatusCode.UnprocessableEntity, second);
        Assert.Contains("Plazas insuficientes", body.GetProperty("violations").ToString(), StringComparison.Ordinal);
        Assert.Equal(0, await AssignedAsync(trip));
        Assert.Equal([AllocationOutcome.RejectedVehicle], await OutcomesAsync(trip));
        Assert.True(waited);
    }

    [Fact]
    public async Task LowerTheSeatsWhileAssigning_WaitsAndTheServiceStays()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        var (first, second, waited) = await CrossAsync(c => Allocate(c, trip, fleet.Drivers[0], fleet.Vehicles[0]), c => Seats(c, fleet.Vehicles[0], 9));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Resources(await ReadTripAsync(trip)));
        Assert.True(waited);
    }

    /// <summary>Two editors that saw the same data: the second one waits and is told, instead of overwriting the first (SPEC 0090 R3).</summary>
    [Fact]
    public async Task SameDriverChangedTwiceAtOnce_OnlyTheFirstIsSaved()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);

        var (first, second, waited) = await CrossAsync(c => Rename(c, fleet.Drivers[0], "Primera"), c => Rename(c, fleet.Drivers[0], "Segunda"));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectDetailsChangedAsync(second);
        await using var read = _db.CreateContext();
        Assert.Equal("Primera", (await read.Drivers.SingleAsync(d => d.Id == fleet.Drivers[0])).FirstName);
        Assert.True(waited);
    }

    [Fact]
    public async Task SameVehicleChangedTwiceAtOnce_OnlyTheFirstIsSaved()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);

        var (first, second, waited) = await CrossAsync(c => Seats(c, fleet.Vehicles[0], 41), c => Seats(c, fleet.Vehicles[0], 42));

        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectDetailsChangedAsync(second);
        await using var read = _db.CreateContext();
        Assert.Equal(41, (await read.Vehicles.SingleAsync(v => v.Id == fleet.Vehicles[0])).SeatingCapacity);
        Assert.True(waited);
    }

    /// <summary>The agenda takes the same lock but is not one of those data (SPEC 0090 R1).</summary>
    [Fact]
    public async Task DataChangedWhileTheAgendaIs_IsNotAConflict()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);

        var (first, second, waited) = await CrossAsync(c => Leave(c, fleet.Drivers[0]), c => Rename(c, fleet.Drivers[0], "Segunda"));

        await ExpectAsync(HttpStatusCode.OK, first);
        var driver = await ExpectAsync(HttpStatusCode.OK, second);
        Assert.Equal("Segunda", driver.GetProperty("FirstName").GetString());
        Assert.Single(driver.GetProperty("Unavailabilities").EnumerateArray());
        Assert.True(waited);
    }

    /// <summary>The answer of the commit got lost: the repetition must not find its own change and call it somebody else's.</summary>
    [Fact]
    public async Task CommitAnswerLost_ChangingTheData_IsNotAConflictWithItself()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);

        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        await ExpectAsync(HttpStatusCode.OK, await Rename(_a.Client, fleet.Drivers[0], "Luisa"));
        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        await ExpectAsync(HttpStatusCode.OK, await Seats(_a.Client, fleet.Vehicles[0], 41));
    }

    [Fact]
    public async Task WaitExhausted_ChangingTheData_IsRetryableAndChangesNothing()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        using var impatient = new ApiInstance(_db.ConnectionString, lockWaitMilliseconds: 300);

        await using (await _db.HoldAsync($"lof:driver:{fleet.Drivers[0]}", $"lof:vehicle:{fleet.Vehicles[0]}"))
        {
            foreach (var response in new[] { await Rename(impatient.Client, fleet.Drivers[0], "Nadie"), await Seats(impatient.Client, fleet.Vehicles[0], 9) })
                Assert.True((await ExpectAsync(HttpStatusCode.ServiceUnavailable, response)).GetProperty("retryable").GetBoolean());
        }

        await using var after = _db.CreateContext();
        Assert.NotEqual("Nadie", (await after.Drivers.SingleAsync(d => d.Id == fleet.Drivers[0])).FirstName);
        Assert.NotEqual(9, (await after.Vehicles.SingleAsync(v => v.Id == fleet.Vehicles[0])).SeatingCapacity);
        await ExpectAsync(HttpStatusCode.OK, await Rename(impatient.Client, fleet.Drivers[0], "Alguien"));
    }

    /// <summary>Each one moves its service to the other's driver and vehicle: taking them as they come would deadlock.</summary>
    [Fact]
    public async Task CrossedReassignments_BothFinish()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var (t1, t2) = (await TripAsync(fleet, 8, 1, driver: 0, vehicle: 0), await TripAsync(fleet, 11, 1, driver: 1, vehicle: 1));

        Task<HttpResponseMessage> first, second;
        await using (await _db.HoldAsync($"lof:driver:{fleet.Drivers[1]}"))
        {
            first = Allocate(_a.Client, t1, fleet.Drivers[1], fleet.Vehicles[1]);
            await Task.WhenAny(_db.UntilWaitingAsync(1, Timeout()), first);
            second = Allocate(_b.Client, t2, fleet.Drivers[0], fleet.Vehicles[0]);
            await Task.WhenAny(_db.UntilWaitingAsync(2, Timeout()), second);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }

        await ExpectAsync(HttpStatusCode.OK, await first.WaitAsync(Patience));
        await ExpectAsync(HttpStatusCode.OK, await second.WaitAsync(Patience));
        Assert.Equal((fleet.Drivers[1], fleet.Vehicles[1]), Resources(await ReadTripAsync(t1)));
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Resources(await ReadTripAsync(t2)));
    }

    [Theory]
    [InlineData("trip")]
    [InlineData("driver")]
    [InlineData("vehicle")]
    [InlineData("previous-driver")]
    [InlineData("previous-vehicle")]
    public async Task WaitExhausted_IsARetryableErrorThatChangesNothingAndIsNotABlock(string busy)
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var trip = await TripAsync(fleet, 8, 2, driver: 1, vehicle: 1);
        var resource = busy switch
        {
            "trip" => $"lof:trip:{trip}",
            "driver" => $"lof:driver:{fleet.Drivers[0]}",
            "vehicle" => $"lof:vehicle:{fleet.Vehicles[0]}",
            "previous-driver" => $"lof:driver:{fleet.Drivers[1]}",
            _ => $"lof:vehicle:{fleet.Vehicles[1]}"
        };
        using var impatient = new ApiInstance(_db.ConnectionString, lockWaitMilliseconds: 300);

        await using (await _db.HoldAsync(resource))
        {
            var response = await Allocate(impatient.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]);

            var body = await ExpectAsync(HttpStatusCode.ServiceUnavailable, response);
            Assert.True(body.GetProperty("retryable").GetBoolean());
            Assert.False(body.TryGetProperty("forcible", out _));
            Assert.Contains("No se ha guardado nada", body.GetProperty("detail").GetString());
            Assert.NotNull(response.Headers.RetryAfter);
            Assert.Equal((fleet.Drivers[1], fleet.Vehicles[1]), Resources(await ReadTripAsync(trip)));
            Assert.Empty(await OutcomesAsync(trip));
        }

        await ExpectAsync(HttpStatusCode.OK, await Allocate(impatient.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]));
        Assert.Equal([AllocationOutcome.Assigned], await OutcomesAsync(trip));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("delete-booking")]
    [InlineData("leave")]
    [InlineData("workshop")]
    public async Task WaitExhausted_InTheOtherOperations_IsAlsoRetryable(string kind)
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2, driver: 0, vehicle: 0);
        int booking;
        await using (var read = _db.CreateContext()) booking = (await read.Trips.SingleAsync(t => t.Id == trip)).BookingId;
        using var impatient = new ApiInstance(_db.ConnectionString, lockWaitMilliseconds: 300);

        await using (await _db.HoldAsync($"lof:driver:{fleet.Drivers[0]}", $"lof:vehicle:{fleet.Vehicles[0]}"))
        {
            var response = kind switch
            {
                "cancel" => await impatient.Client.PostAsync($"/api/trips/{trip}/cancel", null),
                "delete-booking" => await impatient.Client.DeleteAsync($"/api/bookings/{booking}"),
                "leave" => await Leave(impatient.Client, fleet.Drivers[0]),
                _ => await Workshop(impatient.Client, fleet.Vehicles[0])
            };
            Assert.True((await ExpectAsync(HttpStatusCode.ServiceUnavailable, response)).GetProperty("retryable").GetBoolean());
        }

        Assert.Equal(TripStatus.Assigned, (await ReadTripAsync(trip)).Status);
        await using var after = _db.CreateContext();
        Assert.False(await after.DriverUnavailabilities.AnyAsync(u => u.DriverId == fleet.Drivers[0]));
        Assert.False(await after.VehicleUnavailabilities.AnyAsync(u => u.VehicleId == fleet.Vehicles[0]));
    }

    [Fact]
    public async Task WaitExhausted_ChangingAnAbsence_IsRetryableAndChangesNothing()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var leave = await UnavailabilityIdAsync(await Leave(_a.Client, fleet.Drivers[0]));
        var workshop = await UnavailabilityIdAsync(await Workshop(_a.Client, fleet.Vehicles[0]));
        using var impatient = new ApiInstance(_db.ConnectionString, lockWaitMilliseconds: 300);

        await using (await _db.HoldAsync($"lof:driver:{fleet.Drivers[0]}", $"lof:vehicle:{fleet.Vehicles[0]}"))
        {
            foreach (var response in new[]
            {
                await ShortenToDeparture(impatient.Client, $"drivers/{fleet.Drivers[0]}", leave, "SickLeave"),
                await ShortenToDeparture(impatient.Client, $"vehicles/{fleet.Vehicles[0]}", workshop, "Repair")
            })
            {
                Assert.True((await ExpectAsync(HttpStatusCode.ServiceUnavailable, response)).GetProperty("retryable").GetBoolean());
            }
        }

        await using var after = _db.CreateContext();
        Assert.Equal(_day.AddDays(1), (await after.DriverUnavailabilities.SingleAsync(u => u.Id == leave)).Period.End);
        Assert.Equal(_day.AddDays(1), (await after.VehicleUnavailabilities.SingleAsync(u => u.Id == workshop)).Period.End);
    }

    /// <summary>Reassigning a forced service saves twice (assignment, and the validation of what it releases): both or none.</summary>
    [Fact]
    public async Task FailureHalfway_WritesNothingAndFreesEverything()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var source = await TripAsync(fleet, 10, 2);
        await ExpectAsync(HttpStatusCode.OK, await Allocate(_a.Client, source, fleet.Drivers[0], fleet.Vehicles[0], force: true));

        _a.Saves.Fail(n => n == 2 ? new InvalidOperationException("roto a mitad") : null);
        await ExpectAsync(HttpStatusCode.InternalServerError, await Allocate(_a.Client, source, fleet.Drivers[1], fleet.Vehicles[1]));

        var final = await ReadTripAsync(source);
        Assert.Equal((fleet.Drivers[0], fleet.Vehicles[0]), Resources(final));
        Assert.True(final.IsForced);
        Assert.Equal(source, Assert.Single((await ReadTripAsync(later)).ForcedByTrips).SourceTripId);

        await ExpectAsync(HttpStatusCode.OK, await Allocate(_b.Client, source, fleet.Drivers[1], fleet.Vehicles[1]));
        Assert.Empty((await ReadTripAsync(later)).ForcedByTrips);
    }

    /// <summary>EF retries transient failures by itself: the whole operation starts again from what is saved.</summary>
    [Fact]
    public async Task TransientFailureHalfway_IsRetriedFromTheBeginning()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var source = await TripAsync(fleet, 10, 2);
        await ExpectAsync(HttpStatusCode.OK, await Allocate(_a.Client, source, fleet.Drivers[0], fleet.Vehicles[0], force: true));

        _a.Saves.Fail(n => n == 2 ? new TimeoutException("corte") : null);
        await ExpectAsync(HttpStatusCode.OK, await Allocate(_a.Client, source, fleet.Drivers[1], fleet.Vehicles[1]));

        Assert.Equal((fleet.Drivers[1], fleet.Vehicles[1]), Resources(await ReadTripAsync(source)));
        Assert.False((await ReadTripAsync(source)).IsForced);
        Assert.Empty((await ReadTripAsync(later)).ForcedByTrips);
        Assert.True((await ReadTripAsync(later)).LegalValidation!.IsCompliant);
        Assert.Equal([AllocationOutcome.Forced, AllocationOutcome.Assigned], await OutcomesAsync(source));
    }

    /// <summary>The connection breaks after the commit: the operation is already saved and must not be repeated.</summary>
    [Fact]
    public async Task ConnectionLostAfterTheCommit_IsNotRepeated()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var later = await TripAsync(fleet, 12, 4, driver: 0, vehicle: 0);
        var source = await TripAsync(fleet, 10, 2);

        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        await ExpectAsync(HttpStatusCode.OK, await Leave(_a.Client, fleet.Drivers[0]));
        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        await ExpectAsync(HttpStatusCode.OK, await Workshop(_a.Client, fleet.Vehicles[0]));
        int leave, workshop;
        await using (var read = _db.CreateContext())
        {
            leave = (await read.DriverUnavailabilities.SingleAsync(u => u.DriverId == fleet.Drivers[0])).Id;
            workshop = (await read.VehicleUnavailabilities.SingleAsync(u => u.VehicleId == fleet.Vehicles[0])).Id;
        }

        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        Assert.Equal(HttpStatusCode.NoContent, (await _a.Client.DeleteAsync($"/api/drivers/{fleet.Drivers[0]}/unavailabilities/{leave}")).StatusCode);
        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        Assert.Equal(HttpStatusCode.NoContent, (await _a.Client.DeleteAsync($"/api/vehicles/{fleet.Vehicles[0]}/unavailabilities/{workshop}")).StatusCode);

        _a.Commits.FailOnceAfterCommit(new TimeoutException("corte tras confirmar"));
        var forced = await ExpectAsync(HttpStatusCode.OK, await Allocate(_a.Client, source, fleet.Drivers[0], fleet.Vehicles[0], force: true));
        Assert.True(forced.GetProperty("Forced").GetBoolean());
        Assert.Equal([AllocationOutcome.Forced], await OutcomesAsync(source));
        Assert.Equal(source, Assert.Single((await ReadTripAsync(later)).ForcedByTrips).SourceTripId);
    }

    /// <summary>A client that stops waiting frees its place: nothing is written and the resource is not left taken.</summary>
    [Fact]
    public async Task ClientGivesUpWaiting_WritesNothingAndFreesItsPlace()
    {
        var fleet = await FleetAsync(drivers: 1, vehicles: 1);
        var trip = await TripAsync(fleet, 8, 2);

        await using (await _db.HoldAsync($"lof:driver:{fleet.Drivers[0]}"))
        {
            using var giveUp = new CancellationTokenSource();
            var request = Allocate(_a.Client, trip, fleet.Drivers[0], fleet.Vehicles[0], cancellationToken: giveUp.Token);
            await _db.UntilWaitingAsync(1, Timeout());
            await giveUp.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            while (await _db.WaitersAsync() > 0) await Task.Delay(20, Timeout());
        }

        Assert.Equal(0, await AssignedAsync(trip));
        await ExpectAsync(HttpStatusCode.OK, await Allocate(_b.Client, trip, fleet.Drivers[0], fleet.Vehicles[0]));
    }

    /// <summary>Different driver, vehicle and service: nobody waits for anybody.</summary>
    [Fact]
    public async Task UnrelatedAssignments_DoNotWait()
    {
        var fleet = await FleetAsync(drivers: 2, vehicles: 2);
        var (t1, t2) = (await TripAsync(fleet, 8, 2), await TripAsync(fleet, 8, 2));

        var (first, second, waited) = await CrossAsync(
            c => Allocate(c, t1, fleet.Drivers[0], fleet.Vehicles[0]), c => Allocate(c, t2, fleet.Drivers[1], fleet.Vehicles[1]));

        Assert.False(waited);
        await ExpectAsync(HttpStatusCode.OK, first);
        await ExpectAsync(HttpStatusCode.OK, second);
    }

    internal sealed record Fleet(int[] Drivers, int[] Vehicles, int Origin, int Destination);

    /// <summary>Drivers with a verifiable weekly rest behind (a short service nine days before) and nothing else.</summary>
    private Task<Fleet> FleetAsync(int drivers, int vehicles) => FleetAsync(_db, _day, drivers, vehicles);

    internal static async Task<Fleet> FleetAsync(ConcurrencyDatabase db, DateTime day, int drivers, int vehicles)
    {
        await using var write = db.CreateContext();
        var newDrivers = Enumerable.Range(0, drivers).Select(_ => TripRepositoryTests.NewDriver()).ToArray();
        var newVehicles = Enumerable.Range(0, vehicles).Select(_ => TripRepositoryTests.NewVehicle()).ToArray();
        var (origin, destination) = (TripRepositoryTests.NewLocation("Origen"), TripRepositoryTests.NewLocation("Destino"));
        var parking = TripRepositoryTests.NewVehicle(); // the past services use another vehicle
        write.AddRange(newDrivers);
        write.AddRange(newVehicles);
        write.AddRange(origin, destination, parking);
        await write.SaveChangesAsync();
        foreach (var driver in newDrivers)
        {
            var past = new Trip(origin, destination, new TimeWindow(day.AddDays(-9).AddHours(8), day.AddDays(-9).AddHours(10)), 10, ServiceType.Regular);
            past.AssignResources(driver, parking);
            past.Complete();
            write.Trips.Add(past);
        }

        await write.SaveChangesAsync();
        return new Fleet([.. newDrivers.Select(x => x.Id)], [.. newVehicles.Select(x => x.Id)], origin.Id, destination.Id);
    }

    private Task<int> TripAsync(Fleet fleet, double startHour, double hours, int? driver = null, int? vehicle = null) =>
        TripAsync(_db, _day, fleet, startHour, hours, driver, vehicle);

    internal static async Task<int> TripAsync(ConcurrencyDatabase db, DateTime day, Fleet fleet, double startHour, double hours, int? driver = null, int? vehicle = null)
    {
        await using var write = db.CreateContext();
        var trip = new Trip((await write.Locations.FindAsync(fleet.Origin))!, (await write.Locations.FindAsync(fleet.Destination))!,
            new TimeWindow(day.AddHours(startHour), day.AddHours(startHour + hours)), 10, ServiceType.Regular);
        if (driver is { } dr && vehicle is { } ve)
        {
            trip.AssignResources((await write.Drivers.FindAsync(fleet.Drivers[dr]))!, (await write.Vehicles.FindAsync(fleet.Vehicles[ve]))!);
            trip.SetLegalValidation(LegalValidation.Compliant());
        }

        write.Trips.Add(trip);
        await write.SaveChangesAsync();
        return trip.Id;
    }

    /// <summary>
    /// Stops <paramref name="first"/> (instance A) right before writing, lets <paramref name="second"/> (instance B) run until it
    /// finishes or waits for a lock, and then lets A go on. Says whether B had to wait.
    /// </summary>
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second, bool Waited)> CrossAsync(
        Func<HttpClient, Task<HttpResponseMessage>> first, Func<HttpClient, Task<HttpResponseMessage>> second)
    {
        var pause = _a.Saves.PauseNext();
        var firstCall = first(_a.Client);
        if (await Task.WhenAny(pause.Reached, firstCall).WaitAsync(Patience) == firstCall)
        {
            Assert.Fail($"The first request never got to write: {await (await firstCall).Content.ReadAsStringAsync()}");
        }

        using var stop = new CancellationTokenSource(Patience);
        var secondCall = second(_b.Client);
        var waiting = _db.UntilWaitingAsync(1, stop.Token);
        var waited = await Task.WhenAny(secondCall, waiting) == waiting;
        pause.Release();
        var (resultA, resultB) = (await firstCall.WaitAsync(Patience), await secondCall.WaitAsync(Patience));
        await stop.CancelAsync();
        return (resultA, resultB, waited);
    }

    internal static Task<HttpResponseMessage> Allocate(
        HttpClient client, int trip, int driver, int vehicle, bool force = false, CancellationToken cancellationToken = default) =>
        client.AllocateAsync(trip, driver, vehicle, "test", force, force ? Reason : null, cancellationToken: cancellationToken);

    private Task<HttpResponseMessage> Workshop(HttpClient client, int vehicle) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicle}/unavailabilities", new { Start = _day, End = _day.AddDays(1), Type = "Repair" });

    private Task<HttpResponseMessage> Leave(HttpClient client, int driver) =>
        client.PostAsJsonAsync($"/api/drivers/{driver}/unavailabilities", new { Start = _day, End = _day.AddDays(1), Type = "SickLeave" });

    /// <summary>The service leaves at 08:00.</summary>
    private Task<HttpResponseMessage> ShortenToDeparture(HttpClient client, string resource, int unavailability, string type) =>
        client.PutAsJsonAsync($"/api/{resource}/unavailabilities/{unavailability}", new { Start = _day, End = _day.AddHours(8), Type = type });

    private static async Task<HttpResponseMessage> Seats(HttpClient client, int vehicle, int seats)
    {
        var url = $"/api/vehicles/{vehicle}";
        return await client.PutAsJsonAsync(url, new
        {
            LicensePlate = $"PL-{vehicle}", Brand = "Irizar", Model = "i6", SeatingCapacity = seats,
            IsExternal = false, AllowsAutoAssignment = true, HomeLocationId = (int?)null, BaseId = OperatingBase.DefaultId,
            DetailsVersion = await client.DetailsVersionAsync(url)
        });
    }

    private static async Task<HttpResponseMessage> Rename(HttpClient client, int driver, string firstName)
    {
        var url = $"/api/drivers/{driver}";
        return await client.PutAsJsonAsync(url, new
        {
            IdentificationNumber = $"ID-{driver}", FirstName = firstName, LastName = "Pérez",
            IsExternal = false, AllowsAutoAssignment = true, HomeLocationId = (int?)null, BaseId = OperatingBase.DefaultId,
            DetailsVersion = await client.DetailsVersionAsync(url)
        });
    }

    private static async Task ExpectDetailsChangedAsync(HttpResponseMessage response)
    {
        var problem = await ExpectAsync(HttpStatusCode.Conflict, response);
        Assert.Equal("Otra persona ha cambiado estos datos", problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("field", out _));
    }

    private static async Task<int> UnavailabilityIdAsync(HttpResponseMessage response) =>
        (await ExpectAsync(HttpStatusCode.OK, response)).GetProperty("Unavailabilities")[0].GetProperty("Id").GetInt32();

    private static async Task<JsonElement> ExpectAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<Trip> ReadTripAsync(int id)
    {
        await using var read = _db.CreateContext();
        return await read.Trips.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private static (int?, int?) Resources(Trip trip) => (trip.AssignedDriverId, trip.AssignedVehicleId);

    private async Task<int> AssignedAsync(params int[] trips)
    {
        await using var read = _db.CreateContext();
        return await read.Trips.CountAsync(t => trips.Contains(t.Id) && t.AssignedDriverId != null);
    }

    private async Task<AllocationOutcome[]> OutcomesAsync(int trip)
    {
        await using var read = _db.CreateContext();
        return [.. await read.AllocationAttempts.Where(x => x.TripId == trip).OrderBy(x => x.Id).Select(x => x.Outcome).ToListAsync()];
    }

    private Task NoOrphanMarksAsync() => NoOrphanMarksAsync(_db);

    /// <summary>Every forced mark left on a later service has a source alive and with the same driver (SPEC 0063 R8).</summary>
    internal static async Task NoOrphanMarksAsync(ConcurrencyDatabase db)
    {
        await using var read = db.CreateContext();
        var trips = await read.Trips.AsNoTracking().ToListAsync();
        foreach (var trip in trips.Where(t => t.IsActive))
        {
            foreach (var mark in trip.ForcedByTrips)
            {
                var source = trips.Single(t => t.Id == mark.SourceTripId);
                Assert.True(source.IsActive && source.AssignedDriverId == trip.AssignedDriverId, $"#{trip.Id} keeps a mark of #{source.Id}");
            }
        }
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(Patience).Token;
}
