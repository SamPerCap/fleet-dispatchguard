using System.Reflection;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Control;

public class ScheduleStateTests
{
    private static readonly DateTime Start = new(2035, 1, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void InProgressBySchedule_CoveredInsideWindow_True() =>
        Assert.True(ControlRules.InProgressBySchedule(CoveredTrip(), Start.AddMinutes(59)));

    [Fact]
    public void InProgressBySchedule_AtStart_TrueAndAtEnd_False()
    {
        var trip = CoveredTrip();
        Assert.True(ControlRules.InProgressBySchedule(trip, Start));
        Assert.False(ControlRules.InProgressBySchedule(trip, Start.AddHours(1)));
        Assert.True(ControlRules.CompletedBySchedule(trip, Start.AddHours(1)));
    }

    [Fact]
    public void InProgressBySchedule_Uncovered_False_AndNeverCompleted()
    {
        var trip = NewTrip();
        Assert.False(ControlRules.InProgressBySchedule(trip, Start.AddMinutes(30)));
        Assert.False(ControlRules.CompletedBySchedule(trip, Start.AddHours(5)));
    }

    [Fact]
    public void CompletedStatus_CountsAsCompletedNotRunning()
    {
        var trip = CoveredTrip();
        trip.Complete();
        Assert.True(ControlRules.CompletedBySchedule(trip, Start.AddMinutes(30)));
        Assert.False(ControlRules.InProgressBySchedule(trip, Start.AddMinutes(30)));
    }

    [Fact]
    public void OperationalDay_JustAfterMadridMidnight_IsTheMadridDay()
    {
        // 00:30 in Madrid on 5 Feb (CET, UTC+1) = 23:30Z on 4 Feb.
        var (from, to) = OperationalDay.Containing(new DateTime(2036, 2, 4, 23, 30, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2036, 2, 4, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2036, 2, 5, 23, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(DateTimeKind.Utc, from.Kind);
    }

    [Fact]
    public void OperationalDay_Summer_UsesCest()
    {
        // 12:00Z on 1 Jul is 14:00 in Madrid (CEST, UTC+2): the day runs 22:00Z → 22:00Z.
        var (from, to) = OperationalDay.Containing(new DateTime(2036, 7, 1, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2036, 6, 30, 22, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2036, 7, 1, 22, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void OperationalDay_DstChangeDay_Has23Hours()
    {
        // 29 Mar 2036 (last Sunday of March): clocks go 02:00 → 03:00 in Madrid.
        var (from, to) = OperationalDay.Containing(new DateTime(2036, 3, 30, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(23), to - from);
    }

    [Fact]
    public void OperationalDay_Before_IsThePreviousMadridDay()
    {
        var instant = new DateTime(2036, 2, 4, 23, 30, 0, DateTimeKind.Utc);
        var (from, to) = OperationalDay.Before(instant);
        Assert.Equal(new DateTime(2036, 2, 3, 23, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(OperationalDay.Containing(instant).From, to);
    }

    private static Trip NewTrip()
    {
        var origin = new Location("O", "Calle 1", new GeoCoordinate(40.4m, -3.7m), "Madrid", "Madrid");
        var destination = new Location("D", "Calle 2", new GeoCoordinate(40.5m, -3.7m), "Madrid", "Madrid");
        SetId(origin, 1); SetId(destination, 2);
        var trip = new Trip(origin, destination, new TimeWindow(Start, Start.AddHours(1)), 10, ServiceType.Regular);
        SetId(trip, 10);
        return trip;
    }

    private static Trip CoveredTrip()
    {
        var trip = NewTrip();
        var driver = new Driver("ID1", "Ana", "Gil");
        var vehicle = new Vehicle("1234LMN", "Irizar", "i6", 50);
        SetId(driver, 1); SetId(vehicle, 1);
        trip.AssignResources(driver, vehicle);
        return trip;
    }

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
}
