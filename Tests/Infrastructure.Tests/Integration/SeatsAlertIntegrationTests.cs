using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Services of the day that do not fit in their vehicle (SPEC 0081). Each test has its own base with one bus of 55 seats and its
/// services: 08:00–09:00 (55 seats), 12:00–13:00 (50), 14:00–15:00 (40) and, the next day, 12:00–13:00 (55).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SeatsAlertIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly DateTime _day = new(2046, 2, 5, 0, 0, 0, DateTimeKind.Utc);
    private int _base, _vehicle, _noon, _afternoon;

    public SeatsAlertIntegrationTests(PostgreSqlFixture db)
    {
        _db = db;
        _factory = new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
    }

    public async Task InitializeAsync()
    {
        await using var write = _db.CreateContext();
        var operatingBase = new OperatingBase($"S-{Guid.NewGuid():N}"[..20]);
        write.Add(operatingBase);
        await write.SaveChangesAsync();

        var (origin, destination) = (TripRepositoryTests.NewLocation("Origen"), TripRepositoryTests.NewLocation("Destino"));
        var (driver, vehicle) = (TripRepositoryTests.NewDriver(), TripRepositoryTests.NewVehicle());
        driver.MoveToBase(operatingBase.Id);
        vehicle.MoveToBase(operatingBase.Id);
        Trip NewTrip(double hour, int seats)
        {
            var start = _day.AddHours(hour);
            var trip = new Trip(origin, destination, new TimeWindow(start, start.AddHours(1)), seats, ServiceType.Discretionary);
            trip.MoveToBase(operatingBase.Id);
            trip.AssignResources(driver, vehicle);
            return trip;
        }

        var (noon, afternoon) = (NewTrip(12, 50), NewTrip(14, 40));
        write.AddRange(origin, destination, driver, vehicle, NewTrip(8, 55), noon, afternoon, NewTrip(36, 55));
        await write.SaveChangesAsync();
        (_base, _vehicle, _noon, _afternoon) = (operatingBase.Id, vehicle.Id, noon.Id, afternoon.Id);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData(50, false)]
    [InlineData(49, true)]
    public async Task Criterion_IsTheOneOfAllocating(int seats, bool alert)
    {
        await ChangeVehicleAsync(v => Seats(v, seats));

        var alerts = await AlertsAsync(_day.AddHours(10));

        if (!alert)
        {
            Assert.Empty(alerts);
            return;
        }

        var single = Assert.Single(alerts);
        Assert.Equal((ControlAlertType.InsufficientSeats, "Vehicle", _vehicle), (single.Type, single.EntityType, single.EntityId));
        Assert.Equal(("Cambiar de vehículo", $"Tiene 49 plazas y el servicio {_noon} necesita 50"), (single.SuggestedAction.Es(), single.Detail.Es()));
        Assert.Equal(_day.AddHours(12), single.Deadline);
    }

    /// <summary>The one that ended and the one of another day never count.</summary>
    [Fact]
    public async Task SeveralServices_AreOneAlertWithTheFirstDeparture()
    {
        await ChangeVehicleAsync(v => Seats(v, 1));

        var single = Assert.Single(await AlertsAsync(_day.AddHours(10)));
        var incident = Assert.Single(await IncidentsAsync(_day.AddHours(10)));

        Assert.Equal((_day.AddHours(12), "Tiene 1 plaza y 2 servicios necesitan más, hasta 50"), (single.Deadline!.Value, single.Detail.Es()));
        Assert.Equal([_noon, _afternoon], incident.AffectedTrips.Select(t => t.TripId));
        Assert.Equal((2, 90), (incident.AffectedTripCount, incident.AffectedSeats));
    }

    [Theory]
    [InlineData(660, ControlAlertSeverity.Warning)]
    [InlineData(661, ControlAlertSeverity.Urgent)]
    [InlineData(750, ControlAlertSeverity.Urgent)]
    [InlineData(780, null)]
    public async Task Severity_ByTheDeparture_UntilTheServiceEnds(int minuteOfTheDay, ControlAlertSeverity? severity)
    {
        await ChangeVehicleAsync(v => Seats(v, 49));

        var alerts = await AlertsAsync(_day.AddMinutes(minuteOfTheDay));

        Assert.Equal(severity, alerts.SingleOrDefault()?.Severity);
    }

    [Fact]
    public async Task Summary_CountsIt()
    {
        await ChangeVehicleAsync(v => Seats(v, 55));
        Assert.Equal(("Green", 0), await LightAsync(_day.AddHours(10)));

        await ChangeVehicleAsync(v => Seats(v, 49));
        Assert.Equal(("Amber", 1), await LightAsync(_day.AddHours(10)));
        Assert.Equal(("Red", 1), await LightAsync(_day.AddHours(11.5)));
    }

    /// <summary>With an agenda conflict in one service, or out of service right now, it is still one incident that says both.</summary>
    [Theory]
    [InlineData(14, 15, ControlAlertType.UnavailabilityGap)]
    [InlineData(10, 11, ControlAlertType.VehicleUnavailable)]
    public async Task WithAnotherProblemOfTheVehicle_IsTheSameIncident(int from, int to, ControlAlertType other)
    {
        await ChangeVehicleAsync(v =>
        {
            Seats(v, 49);
            v.AddUnavailability(new VehicleUnavailability(v.Id, new TimeWindow(_day.AddHours(from), _day.AddHours(to)), VehicleUnavailabilityType.Repair));
        });

        var incident = Assert.Single(await IncidentsAsync(_day.AddHours(10)));

        Assert.Equal([other, ControlAlertType.InsufficientSeats], incident.Types);
        Assert.Equal([_noon, _afternoon], incident.AffectedTrips.Select(t => t.TripId));
    }

    [Theory]
    [InlineData("seats")]
    [InlineData("cancel")]
    [InlineData("other-vehicle")]
    public async Task GoesAwayAlone(string how)
    {
        await ChangeVehicleAsync(v => Seats(v, 49));
        Assert.Single(await AlertsAsync(_day.AddHours(10)));

        await using (var write = _db.CreateContext())
        {
            var trip = await write.Trips.SingleAsync(t => t.Id == _noon);
            if (how == "cancel") trip.Cancel();
            if (how == "other-vehicle")
            {
                var bigger = TripRepositoryTests.NewVehicle();
                bigger.MoveToBase(_base);
                write.Add(bigger);
                await write.SaveChangesAsync();
                trip.AssignResources(await write.Drivers.SingleAsync(d => d.Id == trip.AssignedDriverId), bigger);
            }

            await write.SaveChangesAsync();
        }

        if (how == "seats") await ChangeVehicleAsync(v => Seats(v, 50));
        Assert.Empty(await AlertsAsync(_day.AddHours(10)));
    }

    private static void Seats(Vehicle vehicle, int seats) =>
        vehicle.ChangeDetails(vehicle.LicensePlate, vehicle.Brand, vehicle.Model, seats, vehicle.IsExternal, vehicle.AllowsAutoAssignment);

    private async Task ChangeVehicleAsync(Action<Vehicle> change)
    {
        await using var write = _db.CreateContext();
        change(await write.Vehicles.SingleAsync(v => v.Id == _vehicle));
        await write.SaveChangesAsync();
    }

    private Task<IReadOnlyList<ControlAlertDto>> AlertsAsync(DateTime now) => AskAsync(async control => (await control.AlertsAsync(_base, _day.AddHours(10), now, 1, 50)).Items);

    private Task<IReadOnlyList<ControlIncidentDto>> IncidentsAsync(DateTime now) => AskAsync(control => control.IncidentsAsync(_base, _day.AddHours(10), now));

    private Task<(string Light, int Tasks)> LightAsync(DateTime now) => AskAsync(async control =>
    {
        var summary = await control.SummaryAsync(_base, _day.AddHours(10), now);
        return (summary.Light, summary.UrgentAlerts + summary.WarningAlerts);
    });

    private async Task<T> AskAsync<T>(Func<IControlCenterService, Task<T>> question)
    {
        using var scope = _factory.Services.CreateScope();
        return await question(scope.ServiceProvider.GetRequiredService<IControlCenterService>());
    }
}
