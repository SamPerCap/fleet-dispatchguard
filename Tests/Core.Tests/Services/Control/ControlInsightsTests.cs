using System.Reflection;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Control;

public class ControlInsightsTests
{
    // 4 Feb 2036, Madrid in CET (UTC+1): the operational day runs 3 Feb 23:00Z → 4 Feb 23:00Z.
    private static readonly DateTime DayFrom = new(2036, 2, 3, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayTo = DayFrom.AddDays(1);

    private int _ids = 100;

    [Fact]
    public void Days_EndWithTheDayOfTheDate_OldestFirst()
    {
        var days = ControlTrends.Days(DayFrom.AddHours(12), 7);

        Assert.Equal(7, days.Count);
        Assert.Equal((DayFrom, DayTo), days[^1]);
        Assert.Equal(DayFrom.AddDays(-6), days[0].From);
        Assert.All(days.Zip(days.Skip(1)), p => Assert.Equal(p.First.To, p.Second.From));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void Days_OutOfRange_Throws(int days) =>
        Assert.ThrowsAny<ArgumentException>(() => ControlTrends.Days(DayFrom, days));

    [Theory]
    [InlineData(2)]
    [InlineData(31)]
    public void Days_Limits_Accepted(int days) => Assert.Equal(days, ControlTrends.Days(DayFrom, days).Count);

    [Fact]
    public void Daily_TripAcrossMidnight_CountsOnBothDays_AsTheSummaryDoes()
    {
        var days = ControlTrends.Days(DayFrom.AddHours(1), 2);
        var midnight = Trip(DayFrom.AddMinutes(-30), DayFrom.AddMinutes(30)); // 23:30–00:30 Madrid
        var covered = Covered(DayFrom.AddHours(10), DayFrom.AddHours(11));
        var cancelled = Trip(DayFrom.AddHours(12), DayFrom.AddHours(13));
        cancelled.Cancel();

        var daily = ControlTrends.Daily([midnight, covered, cancelled], days);

        Assert.Equal(new DailyTripsDto(DayFrom.AddDays(-1), 1, 0), daily[0]);
        Assert.Equal(new DailyTripsDto(DayFrom, 2, 1), daily[1]);
    }

    [Fact]
    public void Daily_EndAtDayStart_NotCounted()
    {
        var days = ControlTrends.Days(DayFrom.AddHours(1), 2);
        var daily = ControlTrends.Daily([Trip(DayFrom.AddHours(-2), DayFrom)], days);
        Assert.Equal([1, 0], daily.Select(d => d.TotalTrips));
    }

    [Fact]
    public void Hourly_SplitsByHour_AndEachHourAddsUp()
    {
        var nine = DayFrom.AddHours(10); // 09:00 Madrid
        var attempts = new[]
        {
            Attempt(nine.AddMinutes(10), AllocationOutcome.Assigned),
            Attempt(nine.AddMinutes(50), AllocationOutcome.RejectedCompliance),
            Attempt(nine.AddHours(1), AllocationOutcome.Assigned), // exactly 10:00 → the 10 o'clock slot
            Attempt(nine.AddHours(2), AllocationOutcome.Forced),
            Attempt(DayTo, AllocationOutcome.Assigned), // next day: out
        };

        var hours = ControlTrends.Hourly(attempts, DayFrom, DayTo);

        Assert.Equal(24, hours.Count);
        Assert.Equal(new EngineHourDto(nine, 2, 1, 1, 0), hours[10]);
        Assert.Equal(new EngineHourDto(nine.AddHours(1), 1, 1, 0, 0), hours[11]);
        Assert.Equal(new EngineHourDto(nine.AddHours(2), 1, 0, 0, 1), hours[12]);
        Assert.All(hours, h => Assert.Equal(h.Validations, h.Accepted + h.Blocked + h.Forced));
        Assert.Equal(4, hours.Sum(h => h.Validations));
    }

    [Fact]
    public void Hours_DstDays_Have23And25Slots()
    {
        var spring = OperationalDay.Containing(new DateTime(2036, 3, 30, 10, 0, 0, DateTimeKind.Utc));
        var autumn = OperationalDay.Containing(new DateTime(2036, 10, 26, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(23, ControlTrends.Hours(spring.From, spring.To).Count);
        Assert.Equal(25, ControlTrends.Hours(autumn.From, autumn.To).Count);
    }

    /// <summary>06:00–22:00 Madrid on 4 Feb 2036 (CET): 05:00Z–21:00Z.</summary>
    private static readonly OperatingHoursSettings Default = new();
    private static (DateTime, DateTime) Day(Vehicle vehicle) => Default.On(DayFrom, vehicle.BaseId);
    private static readonly DateTime Six = DayFrom.AddHours(6);

    [Fact]
    public void OperatingDay_DefaultIs06To22Madrid_AndBaseOverride()
    {
        Assert.Equal((Six, Six.AddHours(16)), Default.On(DayFrom, 1));
        var custom = new OperatingHoursSettings { Bases = { [2] = new BaseOperatingHours { Start = new(7, 0), End = new(15, 30) } } };
        Assert.Equal((Six.AddHours(1), Six.AddHours(9.5)), custom.On(DayFrom, 2));
        Assert.Equal((Six, Six.AddHours(16)), custom.On(DayFrom, 1));
    }

    [Fact]
    public void OperatingDay_Summer_UsesCest()
    {
        var summer = OperationalDay.Containing(new DateTime(2036, 7, 1, 12, 0, 0, DateTimeKind.Utc)).From; // 30 Jun 22:00Z
        Assert.Equal((summer.AddHours(6), summer.AddHours(22)), Default.On(summer, 1)); // 04:00Z–20:00Z
    }

    [Fact]
    public void Settings_EndNotAfterStart_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new OperatingHoursSettings { Start = new(22, 0), End = new(6, 0) }.Validate());
        Assert.Throws<InvalidOperationException>(() => new OperatingHoursSettings
            { Bases = { [3] = new BaseOperatingHours { Start = new(8, 0), End = new(8, 0) } } }.Validate());
        Assert.NotNull(new OperatingHoursSettings().Validate());
    }

    [Fact]
    public void Utilization_SpecExample_FixedDayWithWorkshop()
    {
        // 16 h × 2 vehicles, one in the workshop 06:00–10:00 → 16 + 12 = 28 h available. Covered 4 h + 3 h → 25 %.
        var first = Vehicle(); var second = Vehicle();
        second.AddUnavailability(new VehicleUnavailability(second.Id, new TimeWindow(Six, Six.AddHours(4)), VehicleUnavailabilityType.Repair));
        var trips = new[] { Covered(Six, Six.AddHours(4), first), Covered(Six.AddHours(13), Six.AddHours(16), second) };

        var utilization = ControlEfficiency.Utilization(trips, [first, second], DayFrom, DayTo, Day);

        Assert.Equal((Six, Six.AddHours(16)), (utilization.WindowStart!.Value, utilization.WindowEnd!.Value));
        Assert.Equal(28, utilization.AvailableVehicleHours);
        Assert.Equal(7, utilization.AssignedVehicleHours);
        Assert.Equal(25, utilization.UtilizationPercent);
    }

    [Fact]
    public void Utilization_NoTrips_TheFleetIsStillAvailable()
    {
        var utilization = ControlEfficiency.Utilization([], [Vehicle()], DayFrom, DayTo, Day);
        Assert.Equal((16d, 0d, 0d), (utilization.AvailableVehicleHours, utilization.AssignedVehicleHours, utilization.UtilizationPercent));
        Assert.Equal(24, utilization.VehiclesInServiceByHour.Count);
        Assert.All(utilization.VehiclesInServiceByHour, h => Assert.Equal(0, h.Count));
    }

    [Fact]
    public void Utilization_TripOutsideTheOperatingDay_CountsOnlyInside_ButShowsInTheHourlyBars()
    {
        var vehicle = Vehicle();
        var trip = Covered(Six.AddHours(-2), Six.AddHours(1), vehicle); // 04:00–07:00 Madrid: 1 h inside 06:00–22:00

        var utilization = ControlEfficiency.Utilization([trip], [vehicle], DayFrom, DayTo, Day);

        Assert.Equal(1, utilization.AssignedVehicleHours);
        Assert.Equal(6.3, utilization.UtilizationPercent); // 1/16 = 6.25 → 6.3 (rounding away from zero)
        Assert.Equal(1, utilization.VehiclesInServiceByHour[4].Count); // 04:00 Madrid
    }

    [Fact]
    public void Utilization_TripInsideItsVehicleUnavailability_NeverAbove100()
    {
        var vehicle = Vehicle();
        var start = Six.AddHours(2);
        vehicle.AddUnavailability(new VehicleUnavailability(vehicle.Id, new TimeWindow(start, start.AddHours(15)), VehicleUnavailabilityType.Repair));
        var trip = Covered(start, start.AddHours(2), vehicle);

        var utilization = ControlEfficiency.Utilization([trip], [vehicle], DayFrom, DayTo, Day);

        Assert.Equal(2, utilization.AvailableVehicleHours); // 06:00–08:00 before the workshop
        Assert.Equal(0, utilization.AssignedVehicleHours);   // the trip falls inside the workshop
        Assert.True(utilization.UtilizationPercent <= 100);
    }

    [Fact]
    public void Utilization_VehicleOutOfScopeOrInactive_NotCounted_AndBasesWithDifferentDays_NoSharedWindow()
    {
        var mine = Vehicle(); var other = Vehicle(); var retired = Vehicle();
        retired.Deactivate();
        var trips = new[] { Covered(Six.AddHours(3), Six.AddHours(5), other) };

        var utilization = ControlEfficiency.Utilization(trips, [mine, retired], DayFrom, DayTo, Day);
        Assert.Equal((1, 16d, 0d), (utilization.ActiveVehicles, utilization.AvailableVehicleHours, utilization.AssignedVehicleHours));

        var mixed = ControlEfficiency.Utilization([], [mine, other], DayFrom, DayTo,
            veh => veh.Id == mine.Id ? (Six, Six.AddHours(8)) : (Six, Six.AddHours(16)));
        Assert.Null(mixed.WindowStart);
        Assert.Equal(24, mixed.AvailableVehicleHours);
    }

    [Fact]
    public void Utilization_VehiclesInServiceByHour_CountsDistinctVehicles()
    {
        var busy = Vehicle(); var idle = Vehicle();
        var ten = DayFrom.AddHours(11);
        var trips = new[]
        {
            Covered(ten, ten.AddMinutes(30), busy),
            Covered(ten.AddMinutes(30), ten.AddHours(1.5), busy), // same vehicle, same hour → still 1
            Covered(ten.AddMinutes(15), ten.AddMinutes(45), idle),
        };

        var byHour = ControlEfficiency.Utilization(trips, [busy, idle], DayFrom, DayTo, Day).VehiclesInServiceByHour;

        Assert.Equal(2, byHour[11].Count);
        Assert.Equal(1, byHour[12].Count);
        Assert.Equal(0, byHour[13].Count);
    }

    private Trip Trip(DateTime start, DateTime end)
    {
        var origin = new Location("O", "Calle 1", new GeoCoordinate(40.4m, -3.7m), "Madrid", "Madrid");
        var destination = new Location("D", "Calle 2", new GeoCoordinate(40.5m, -3.7m), "Madrid", "Madrid");
        SetId(origin, 1); SetId(destination, 2);
        var trip = new Trip(origin, destination, new TimeWindow(start, end), 10, ServiceType.Regular);
        SetId(trip, _ids++);
        return trip;
    }

    private Trip Covered(DateTime start, DateTime end, Vehicle? vehicle = null)
    {
        var trip = Trip(start, end);
        var driver = new Driver($"ID{_ids}", "Ana", "Gil");
        SetId(driver, _ids++);
        trip.AssignResources(driver, vehicle ?? Vehicle());
        return trip;
    }

    private Vehicle Vehicle()
    {
        var vehicle = new Vehicle($"{_ids:0000}LMN", "Irizar", "i6", 50);
        SetId(vehicle, _ids++);
        return vehicle;
    }

    private static AllocationAttempt Attempt(DateTime at, AllocationOutcome outcome) => new(at, "Marta", 1, 1, 1, 1, outcome);

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
}
