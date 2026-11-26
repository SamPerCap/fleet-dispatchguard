using System.Globalization;
using System.Net;
using System.Text.Json;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ControlCenterIntegrationTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ControlCenterIntegrationTests(PostgreSqlFixture db)
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
    public async Task Alerts_EachTypeAndSeverity_OnlyFromRequestedBase()
    {
        var day = new DateTime(2036, 2, 4, 0, 0, 0, DateTimeKind.Utc);
        var now = day.AddHours(11).AddMinutes(40);
        var seed = await SeedDayAsync(day, now);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IControlCenterService>();

        var alerts = (await service.AlertsAsync(seed.BaseA, day, now, 1, 50)).Items;

        Assert.Contains(alerts, a => a.Type == ControlAlertType.UnassignedTrip && a.EntityId == seed.UrgentTrip && a.Severity == ControlAlertSeverity.Urgent);
        Assert.Contains(alerts, a => a.Type == ControlAlertType.UnassignedTrip && a.EntityId == seed.LaterTrip && a.Severity == ControlAlertSeverity.Warning);
        Assert.Contains(alerts, a => a.Type == ControlAlertType.PartialBooking && a.EntityId == seed.PartialBooking);
        Assert.Contains(alerts, a => a.Type == ControlAlertType.VehicleUnavailable && a.EntityId == seed.BrokenVehicle);
        Assert.Contains(alerts, a => a.Type == ControlAlertType.UnavailabilityGap && a.EntityId == seed.AbsentDriver && a.EntityType == "Driver");
        Assert.DoesNotContain(alerts, a => a.EntityId == seed.TripInB && a.EntityType == "Trip");
        Assert.Equal(ControlAlertSeverity.Urgent, alerts[0].Severity); // urgent first

        var summaryA = await service.SummaryAsync(seed.BaseA, day, now);
        Assert.Equal("Red", summaryA.Light);
        Assert.Equal(2, summaryA.CoveredTrips);
        Assert.Equal(4, summaryA.TotalTrips);

        var summaryB = await service.SummaryAsync(seed.BaseB, day, day.AddHours(10)); // B's trip leaves at 12:00 → amber
        Assert.Equal("Amber", summaryB.Light);
        Assert.Equal(1, summaryB.TotalTrips);
    }

    [Fact]
    public async Task Endpoints_TwoBases_EachReturnsOnlyItsOwnData()
    {
        var day = new DateTime(2036, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var seed = await SeedDayAsync(day, day.AddHours(11));

        // A human allocation in base A leaves an attempt (R4) that feeds metrics and activity.
        var allocate = await _client.AllocateAsync(seed.LaterTrip, seed.FreeDriver, seed.FreeVehicle, "Marta");
        Assert.Equal(HttpStatusCode.OK, allocate.StatusCode);

        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = DateTime.UtcNow.AddMinutes(-5).ToString("o");
        foreach (var url in new[]
                 {
                     $"summary?date={date}", $"alerts?date={date}", "resources", $"engine-metrics?from={Uri.EscapeDataString(from)}",
                     $"activity?since={Uri.EscapeDataString(from)}"
                 })
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/control/{url}&baseId={seed.BaseA}".Replace("resources&", "resources?"))).StatusCode);
        }

        var metricsA = await GetAsync($"/api/control/engine-metrics?baseId={seed.BaseA}&from={Uri.EscapeDataString(from)}");
        var metricsB = await GetAsync($"/api/control/engine-metrics?baseId={seed.BaseB}&from={Uri.EscapeDataString(from)}");
        Assert.Equal(1, metricsA.GetProperty("Validations").GetInt32());
        Assert.Equal(0, metricsA.GetProperty("ManualAssignments").GetInt32());
        Assert.Equal(0, metricsB.GetProperty("Validations").GetInt32());

        var activityA = await GetAsync($"/api/control/activity?baseId={seed.BaseA}&since={Uri.EscapeDataString(from)}");
        var item = Assert.Single(activityA.GetProperty("Items").EnumerateArray());
        Assert.Equal("Marta", item.GetProperty("Who").GetString());
        Assert.Equal("Assigned", item.GetProperty("Outcome").GetString());
        Assert.Empty((await GetAsync($"/api/control/activity?baseId={seed.BaseB}&since={Uri.EscapeDataString(from)}")).GetProperty("Items").EnumerateArray());

        var resourcesB = await GetAsync($"/api/control/resources?baseId={seed.BaseB}");
        Assert.Equal(1, resourcesB.GetProperty("Drivers").GetProperty("Available").GetInt32());
        Assert.Equal(1, resourcesB.GetProperty("Drivers").GetProperty("Total").GetInt32());
        Assert.Equal(1, resourcesB.GetProperty("Vehicles").GetProperty("Available").GetInt32());

        var summaryB = await GetAsync($"/api/control/summary?baseId={seed.BaseB}&date={date}");
        Assert.Equal(1, summaryB.GetProperty("TotalTrips").GetInt32());

        // Bases for the web filter, and each trip carries its base.
        var bases = (await GetAsync("/api/control/bases")).EnumerateArray().Select(b => b.GetProperty("Id").GetInt32()).ToList();
        Assert.Contains(seed.BaseA, bases);
        Assert.Contains(seed.BaseB, bases);
        Assert.Equal(seed.BaseB, (await GetAsync($"/api/trips/{seed.TripInB}")).GetProperty("BaseId").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/control/summary?baseId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/control/alerts?baseId={seed.BaseA}&page=0")).StatusCode);
    }

    /// <summary>The summary counts the grouped incidents, so tasks = urgent + warnings = incidents (SPEC 0057).</summary>
    [Fact]
    public async Task Incidents_GroupRelatedAlerts_AndSummaryCountsThem()
    {
        var day = new DateTime(2036, 4, 8, 0, 0, 0, DateTimeKind.Utc);
        var now = day.AddHours(11).AddMinutes(40);
        var seed = await SeedDayAsync(day, now);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IControlCenterService>();

        var alerts = (await service.AlertsAsync(seed.BaseA, day, now, 1, 50)).Items;
        var incidents = await service.IncidentsAsync(seed.BaseA, day, now);

        // 6 raw alerts (2 uncovered + 2 incomplete bookings + vehicle + driver) are 4 incidents.
        Assert.Equal(6, alerts.Count);
        Assert.Equal(4, incidents.Count);
        Assert.Equal(["Booking", "Booking", "Vehicle", "Driver"], incidents.Select(i => i.Key.Split(':')[0]));
        Assert.Equal(ControlAlertSeverity.Urgent, incidents[0].Severity);
        Assert.Equal(("Trip", seed.UrgentTrip), (incidents[0].EntityType, incidents[0].EntityId));
        Assert.Equal("Asignar conductor y vehículo", incidents[0].RecommendedAction.Es());
        Assert.All(incidents.Skip(1), i => Assert.Equal(ControlAlertSeverity.Warning, i.Severity));
        var vehicle = incidents.Single(i => i.Key == $"Vehicle:{seed.BrokenVehicle}");
        Assert.Equal(1, vehicle.AffectedTripCount);
        Assert.Equal(10, vehicle.AffectedSeats);

        var summary = await service.SummaryAsync(seed.BaseA, day, now);
        Assert.Equal(1, summary.UrgentAlerts);
        Assert.Equal(3, summary.WarningAlerts);
        Assert.Equal(incidents.Count, summary.UrgentAlerts + summary.WarningAlerts);
        Assert.Equal("Quedan 4 tareas por resolver", summary.Headline.Es());
        Assert.Equal("Red", summary.Light);

        // Once every trip has ended nothing is actionable any more: no incidents, green, but the text does not say that all is covered.
        var night = day.AddHours(20);
        Assert.Empty(await service.IncidentsAsync(seed.BaseA, day, night));
        var late = await service.SummaryAsync(seed.BaseA, day, night);
        Assert.Equal(("Green", "Nada pendiente"), (late.Light, late.Headline.Es()));
    }

    /// <summary>In progress by schedule, the same in summary and resources; states add up to the total (SPEC 0057).</summary>
    [Fact]
    public async Task InProgressBySchedule_SummaryAndResourcesAgree()
    {
        var day = new DateTime(2036, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var seed = await SeedDayAsync(day, day.AddHours(11));
        var at = day.AddHours(16.5); // the 16:00–17:00 trip is running (by schedule; nobody sets InProgress)

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IControlCenterService>();

        var summary = await service.SummaryAsync(seed.BaseA, day, at);
        Assert.Equal(1, summary.InProgressBySchedule);
        Assert.Equal(0, summary.CompletedBySchedule);
        Assert.Equal(1, summary.Upcoming); // the 18:00 trip
        Assert.Equal(day.AddHours(18), summary.NextDeparture);
        Assert.Equal("Servicio 18", summary.NextDepartureLabel.Es()); // The name of the trip at the next departure.
        Assert.Equal(4, summary.TotalTrips);
        Assert.Equal(0, summary.YesterdayTotalTrips);

        var resources = await service.ResourcesAsync(seed.BaseA, at);
        Assert.Equal(new DriverResourcesDto(InService: 1, Assigned: 1, Available: 1, LegallyRestricted: 0, OutOfHours: 0, Unavailable: 0), resources.Drivers);
        Assert.Equal(new VehicleResourcesDto(InService: 1, Assigned: 1, Available: 1, Unavailable: 0), resources.Vehicles);
        Assert.Equal(summary.InProgressBySchedule, resources.Vehicles.InService);

        // At 17:30 the absent driver (17–19) is physically unavailable; the 16–17 trip is completed by schedule.
        var later = day.AddHours(17.5);
        Assert.Equal(1, (await service.ResourcesAsync(seed.BaseA, later)).Drivers.Unavailable);
        Assert.Equal(1, (await service.SummaryAsync(seed.BaseA, day, later)).CompletedBySchedule);
    }

    /// <summary>Every base, validations = accepted + blocked + forced, activity by kind (SPEC 0057).</summary>
    [Fact]
    public async Task Http_AllBases_EngineAddsUp_ActivityByKind()
    {
        var day = new DateTime(2036, 4, 22, 0, 0, 0, DateTimeKind.Utc);
        var seed = await SeedDayAsync(day, day.AddHours(11));
        var from = DateTime.UtcNow.AddMinutes(-5).ToString("o");

        Assert.Equal(HttpStatusCode.OK, (await _client.AllocateAsync(seed.LaterTrip, seed.FreeDriver, seed.FreeVehicle, "Marta")).StatusCode);
        // The vehicle goes to the workshop over the urgent trip → the engine blocks it.
        var repair = await _client.PostAsync($"/api/vehicles/{seed.BrokenVehicle}/unavailabilities", Json(new
        {
            Start = day.AddHours(12), End = day.AddHours(14), Type = "Repair", Notes = (string?)null
        }));
        Assert.True(repair.IsSuccessStatusCode, await repair.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.AllocateAsync(seed.UrgentTrip, seed.FreeDriver, seed.BrokenVehicle, "Luis")).StatusCode);

        var metrics = await GetAsync($"/api/control/engine-metrics?baseId={seed.BaseA}&from={Uri.EscapeDataString(from)}");
        Assert.Equal(2, metrics.GetProperty("Validations").GetInt32());
        Assert.Equal(1, metrics.GetProperty("Accepted").GetInt32());
        Assert.Equal(1, metrics.GetProperty("IllegalBlocked").GetInt32());
        Assert.Equal(metrics.GetProperty("Validations").GetInt32(),
            metrics.GetProperty("Accepted").GetInt32() + metrics.GetProperty("IllegalBlocked").GetInt32() + metrics.GetProperty("Forced").GetInt32());

        var blocked = await GetAsync($"/api/control/activity?baseId={seed.BaseA}&since={Uri.EscapeDataString(from)}&kind=Blocked");
        var item = Assert.Single(blocked.GetProperty("Items").EnumerateArray());
        Assert.Equal("RejectedVehicle", item.GetProperty("Outcome").GetString());
        Assert.Equal("Luis", item.GetProperty("Who").GetString());
        Assert.StartsWith("Servicio 12", item.GetProperty("TripLabel").GetString());
        var assigned = await GetAsync($"/api/control/activity?baseId={seed.BaseA}&since={Uri.EscapeDataString(from)}&kind=Assigned");
        Assert.Equal("Assigned", Assert.Single(assigned.GetProperty("Items").EnumerateArray()).GetProperty("Outcome").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/control/activity?baseId={seed.BaseA}&kind=Nope")).StatusCode);

        // Without baseId: every base. This day only has the two seeded bases.
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var all = await GetAsync($"/api/control/summary?date={date}");
        var summaryA = await GetAsync($"/api/control/summary?baseId={seed.BaseA}&date={date}");
        var summaryB = await GetAsync($"/api/control/summary?baseId={seed.BaseB}&date={date}");
        Assert.Equal(JsonValueKind.Null, all.GetProperty("BaseId").ValueKind);
        Assert.Equal(summaryA.GetProperty("TotalTrips").GetInt32() + summaryB.GetProperty("TotalTrips").GetInt32(), all.GetProperty("TotalTrips").GetInt32());
        foreach (var path in new[] { $"incidents?date={date}", "resources", $"engine-metrics?from={Uri.EscapeDataString(from)}", "activity" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/control/{path}")).StatusCode);
    }

    /// <summary>The trend ends with the summary and the hours add up to engine-metrics (SPEC 0058).</summary>
    [Fact]
    public async Task Http_TrendsAndEngineHourly_AddUpWithSummaryAndMetrics()
    {
        var day = new DateTime(2036, 5, 6, 0, 0, 0, DateTimeKind.Utc);
        var seed = await SeedDayAsync(day, day.AddHours(11));
        var date = Uri.EscapeDataString(day.AddHours(12).ToString("o"));

        var trend = await GetAsync($"/api/control/trends?baseId={seed.BaseA}&date={date}&days=3");
        var summary = await GetAsync($"/api/control/summary?baseId={seed.BaseA}&date={date}");
        var days = trend.GetProperty("Days").EnumerateArray().ToList();
        Assert.Equal(3, days.Count);
        Assert.Equal(summary.GetProperty("TotalTrips").GetInt32(), days[^1].GetProperty("TotalTrips").GetInt32());
        Assert.Equal(summary.GetProperty("CoveredTrips").GetInt32(), days[^1].GetProperty("CoveredTrips").GetInt32());
        Assert.Equal(summary.GetProperty("YesterdayTotalTrips").GetInt32(), days[^2].GetProperty("TotalTrips").GetInt32());
        Assert.Equal((4, 2), (days[^1].GetProperty("TotalTrips").GetInt32(), days[^1].GetProperty("CoveredTrips").GetInt32()));

        // Attempts are recorded at the real "now": one accepted and one blocked.
        Assert.Equal(HttpStatusCode.OK, (await _client.AllocateAsync(seed.LaterTrip, seed.FreeDriver, seed.FreeVehicle, "Marta")).StatusCode);
        var repair = await _client.PostAsync($"/api/vehicles/{seed.BrokenVehicle}/unavailabilities", Json(new
        {
            Start = day.AddHours(12), End = day.AddHours(14), Type = "Repair", Notes = (string?)null
        }));
        Assert.True(repair.IsSuccessStatusCode, await repair.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.AllocateAsync(seed.UrgentTrip, seed.FreeDriver, seed.BrokenVehicle, "Luis")).StatusCode);

        var now = DateTime.UtcNow;
        var hourly = await GetAsync($"/api/control/engine-hourly?baseId={seed.BaseA}&date={Uri.EscapeDataString(now.ToString("o"))}");
        var from = hourly.GetProperty("From").GetDateTime();
        var to = hourly.GetProperty("To").GetDateTime();
        var metrics = await GetAsync($"/api/control/engine-metrics?baseId={seed.BaseA}&from={Uri.EscapeDataString(DateTime.SpecifyKind(from, DateTimeKind.Utc).ToString("o"))}&to={Uri.EscapeDataString(DateTime.SpecifyKind(to, DateTimeKind.Utc).ToString("o"))}");
        var hours = hourly.GetProperty("Hours").EnumerateArray().ToList();
        Assert.InRange(hours.Count, 23, 25);
        Assert.Equal(metrics.GetProperty("Validations").GetInt32(), hours.Sum(h => h.GetProperty("Validations").GetInt32()));
        Assert.Equal(metrics.GetProperty("IllegalBlocked").GetInt32(), hours.Sum(h => h.GetProperty("Blocked").GetInt32()));
        Assert.Equal(2, metrics.GetProperty("Validations").GetInt32());
        Assert.All(hours, h => Assert.Equal(h.GetProperty("Validations").GetInt32(),
            h.GetProperty("Accepted").GetInt32() + h.GetProperty("Blocked").GetInt32() + h.GetProperty("Forced").GetInt32()));

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/control/trends?baseId={seed.BaseA}&days=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/control/trends?baseId={seed.BaseA}&days=32")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/control/engine-hourly?baseId=999999")).StatusCode);
        foreach (var path in new[] { "trends", "engine-hourly", "efficiency" })
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/control/{path}")).StatusCode); // every base
    }

    /// <summary>Utilization over the fixed operating day (06:00–22:00) and the optimizer's saving (SPEC 0058).</summary>
    [Fact]
    public async Task Http_Efficiency_FixedOperatingDayAndOptimizerSaving()
    {
        var day = new DateTime(2036, 5, 13, 0, 0, 0, DateTimeKind.Utc); // CEST: the Madrid day runs 12 May 22:00Z → 13 May 22:00Z
        await using var write = _db.CreateContext();
        var operatingBase = new OperatingBase($"E-{Guid.NewGuid():N}"[..20]);
        write.Add(operatingBase);
        await write.SaveChangesAsync();
        var placeA = new Location("Plaza A", "A 1", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");
        var placeZ = new Location("Plaza Z", "Z 1", new GeoCoordinate(40.5000m, -3.7038m), "Madrid", "Madrid");
        var driver = TripRepositoryTests.NewDriver(); driver.MoveToBase(operatingBase.Id);
        var vehicle = TripRepositoryTests.NewVehicle(); vehicle.MoveToBase(operatingBase.Id);
        var idle = TripRepositoryTests.NewVehicle(); idle.MoveToBase(operatingBase.Id);
        Trip NewTrip(double hour)
        {
            var trip = new Trip(placeA, placeZ, new TimeWindow(day.AddHours(hour), day.AddHours(hour + 1)), 10, ServiceType.Discretionary, $"E {hour}");
            trip.MoveToBase(operatingBase.Id);
            trip.AssignResources(driver, vehicle);
            return trip;
        }
        write.AddRange(placeA, placeZ, driver, vehicle, idle, NewTrip(8), NewTrip(11));
        await write.SaveChangesAsync();

        var efficiency = await GetAsync($"/api/control/efficiency?baseId={operatingBase.Id}&date={Uri.EscapeDataString(day.AddHours(10).ToString("o"))}");

        // 2 vehicles × 16 h (06:00–22:00 Madrid = 04:00Z–20:00Z) = 32 h available; 2 h assigned → 6.3 %.
        Assert.Equal(2, efficiency.GetProperty("ActiveVehicles").GetInt32());
        Assert.Equal(day.AddHours(4), DateTime.SpecifyKind(efficiency.GetProperty("WindowStart").GetDateTime(), DateTimeKind.Utc));
        Assert.Equal(32, efficiency.GetProperty("AvailableVehicleHours").GetDouble());
        Assert.Equal(2, efficiency.GetProperty("AssignedVehicleHours").GetDouble());
        Assert.Equal(6.3, efficiency.GetProperty("FleetUtilizationPercent").GetDouble());
        Assert.Equal(1, efficiency.GetProperty("VehiclesInServiceByHour").EnumerateArray().Max(h => h.GetProperty("Count").GetInt32()));

        // A→Z twice: the second needs the empty leg Z→A (no home base → 0 km to and from it). The optimizer never does worse.
        var leg = placeA.Coordinate.DistanceTo(placeZ.Coordinate);
        Assert.Equal(2, efficiency.GetProperty("TripsConsidered").GetInt32());
        var baseline = efficiency.GetProperty("BaselineDeadheadKm").GetDouble();
        var proposed = efficiency.GetProperty("ProposedDeadheadKm").GetDouble();
        Assert.Equal(Math.Round(leg, 1), baseline);
        Assert.True(proposed <= baseline);
        Assert.Equal(Math.Round((baseline - proposed) / baseline * 100, 0), Math.Round(efficiency.GetProperty("SavingPercent").GetDouble(), 0));
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private async Task<JsonElement> GetAsync(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private sealed record Seed(
        int BaseA, int BaseB, int UrgentTrip, int LaterTrip, int PartialBooking, int BrokenVehicle,
        int AbsentDriver, int TripInB, int FreeDriver, int FreeVehicle);

    /// <summary>
    /// Base A on <paramref name="day"/>: 12:39 unassigned (urgent at 11:40), 14:30 unassigned in a booking
    /// whose 16:00 trip is assigned to a vehicle broken right now, and 18:00 assigned to a driver absent 17–19.
    /// Base B: one unassigned trip at 12:00 plus one free driver and vehicle.
    /// </summary>
    private async Task<Seed> SeedDayAsync(DateTime day, DateTime now)
    {
        await using var write = _db.CreateContext();
        var baseA = new OperatingBase($"A-{Guid.NewGuid():N}"[..20]);
        var baseB = new OperatingBase($"B-{Guid.NewGuid():N}"[..20]);
        write.AddRange(baseA, baseB);
        await write.SaveChangesAsync();

        var origin = TripRepositoryTests.NewLocation("Origen");
        var destination = TripRepositoryTests.NewLocation("Destino");
        Driver NewDriver(OperatingBase operatingBase) { var driver = TripRepositoryTests.NewDriver(); driver.MoveToBase(operatingBase.Id); return driver; }
        Vehicle NewVehicle(OperatingBase operatingBase) { var vehicle = TripRepositoryTests.NewVehicle(); vehicle.MoveToBase(operatingBase.Id); return vehicle; }
        Trip NewTrip(OperatingBase operatingBase, double hour, Booking? booking = null)
        {
            var start = day.AddHours(hour);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(1)), 10, ServiceType.Discretionary, $"Servicio {hour}", booking);
            trip.MoveToBase(operatingBase.Id);
            return trip;
        }

        var goodDriver = NewDriver(baseA); var absentDriver = NewDriver(baseA); var freeDriver = NewDriver(baseA);
        var brokenVehicle = NewVehicle(baseA); var okVehicle = NewVehicle(baseA); var freeVehicle = NewVehicle(baseA);
        var driverB = NewDriver(baseB); var vehicleB = NewVehicle(baseB);
        var booking = new Booking("Cliente", "Congreso");
        var urgent = NewTrip(baseA, 12 + 39 / 60.0);
        var later = NewTrip(baseA, 14.5, booking);
        var brokenTrip = NewTrip(baseA, 16, booking);
        var absentTrip = NewTrip(baseA, 18);
        var tripB = NewTrip(baseB, 12);
        brokenTrip.AssignResources(goodDriver, brokenVehicle);
        absentTrip.AssignResources(absentDriver, okVehicle);
        write.AddRange(origin, destination, goodDriver, absentDriver, freeDriver, brokenVehicle, okVehicle, freeVehicle,
            driverB, vehicleB, booking, urgent, later, brokenTrip, absentTrip, tripB);
        await write.SaveChangesAsync();

        brokenVehicle.AddUnavailability(new VehicleUnavailability(brokenVehicle.Id,
            new TimeWindow(now.AddHours(-1), now.AddHours(1)), VehicleUnavailabilityType.Repair));
        absentDriver.AddUnavailability(new DriverUnavailability(absentDriver.Id,
            new TimeWindow(day.AddHours(17), day.AddHours(19)), UnavailabilityType.SickLeave));
        await write.SaveChangesAsync();

        return new Seed(baseA.Id, baseB.Id, urgent.Id, later.Id, booking.Id, brokenVehicle.Id,
            absentDriver.Id, tripB.Id, freeDriver.Id, freeVehicle.Id);
    }
}
