using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// 24h − daily rest (13h normal, 15h reduced), measured as the span from the start of the first committed work to the end of the
/// last one in the day (SPEC 0030).
/// </summary>
[Trait("Regulation", "Art. 8")]
public class DriverAvailabilityTests
{
    private static readonly DateTime Day = new(2035, 3, 6, 0, 0, 0, DateTimeKind.Utc);

    private static TimeWindow Window(double startHour, double endHour) =>
        new(Day.AddHours(startHour), Day.AddHours(endHour));

    [Fact]
    public void DailyAvailableWindow_NormalRest_Is13Hours()
    {
        var availability = DriverAvailability.For(Day, useReducedDailyRest: false, []);

        Assert.Equal(TimeSpan.FromDays(1) - DrivingLimits.DailyRestNormal, availability.DailyAvailableWindow);
        Assert.Equal(TimeSpan.FromHours(13), availability.DailyAvailableWindow);
    }

    [Fact]
    public void DailyAvailableWindow_ReducedRest_Is15Hours()
    {
        var availability = DriverAvailability.For(Day, useReducedDailyRest: true, []);

        Assert.Equal(TimeSpan.FromHours(15), availability.DailyAvailableWindow);
    }

    [Fact]
    public void RemainingAvailability_NoCommittedWork_EqualsWholeWindow()
    {
        var availability = DriverAvailability.For(Day, false, []);

        Assert.Equal(TimeSpan.FromHours(13), availability.RemainingAvailability);
    }

    [Fact]
    public void RemainingAvailability_CommittedWork_DeductsSpanFromFirstStartToLastEnd()
    {
        // 06–08 and 12–14: span 06–14 = 8h, even though only 4h are worked.
        var availability = DriverAvailability.For(Day, false, [Window(6, 8), Window(12, 14)]);

        Assert.Equal(TimeSpan.FromHours(5), availability.RemainingAvailability);
    }

    [Fact]
    public void RemainingAvailability_WorkOutsideTheDay_IsIgnored()
    {
        var availability = DriverAvailability.For(Day, false, [Window(-10, -8), Window(25, 27)]);

        Assert.Equal(TimeSpan.FromHours(13), availability.RemainingAvailability);
    }

    [Fact]
    public void Fits_TripInsideTheSpan_IsTrue()
    {
        var availability = DriverAvailability.For(Day, false, [Window(6, 8)]);

        Assert.True(availability.Fits(Window(15, 19))); // span 06–19 = 13h
    }

    [Fact]
    public void Fits_TripExceedingRemainingAvailability_IsFalse()
    {
        var availability = DriverAvailability.For(Day, false, [Window(6, 8)]);

        Assert.False(availability.Fits(Window(15, 19.25))); // span 13h15
    }

    [Fact]
    public void Fits_SameTripWithReducedRest_IsTrue()
    {
        var availability = DriverAvailability.For(Day, true, [Window(6, 8)]);

        Assert.True(availability.Fits(Window(15, 19.25)));
    }

    [Fact]
    public void Fits_TripBeforeCommittedWork_CountsTheWholeSpan()
    {
        var availability = DriverAvailability.For(Day, false, [Window(18, 20)]);

        Assert.False(availability.Fits(Window(5, 7))); // span 05–20 = 15h
    }

    [Fact]
    public void Fits_TripLongerThanWindowWithNoCommittedWork_IsFalse()
    {
        var availability = DriverAvailability.For(Day, true, []);

        Assert.False(availability.Fits(Window(4, 19.5))); // 15h30 > 15h
    }

    [Fact]
    public void RemainingAvailability_CommittedSpanBeyondWindow_IsZeroNeverNegative()
    {
        var availability = DriverAvailability.For(Day, false, [Window(4, 6), Window(20, 22)]);

        Assert.Equal(TimeSpan.Zero, availability.RemainingAvailability);
        Assert.True(availability.RemainingAvailability <= availability.DailyAvailableWindow);
    }

    [Fact]
    public void For_NullCommittedWork_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DriverAvailability.For(Day, false, null!));
    }

    private static Driver NewDriver() => new("12345678A", "Ana", "López");

    [Fact]
    public void Driver_HasAvailabilityFor_TripFits_IsTrue()
    {
        var driver = NewDriver();

        Assert.True(driver.HasAvailabilityFor(Window(8, 12), [Window(6, 7)]));
    }

    [Fact]
    public void Driver_HasAvailabilityFor_TripExceedsAvailability_IsFalse()
    {
        var driver = NewDriver();

        Assert.False(driver.HasAvailabilityFor(Window(18, 20), [Window(6, 8)]));
    }

    [Fact]
    public void Driver_HasAvailabilityFor_TripOverlapsUnavailability_IsFalse()
    {
        var driver = NewDriver();
        driver.AddUnavailability(new DriverUnavailability(1, Window(9, 11), UnavailabilityType.SickLeave));

        Assert.False(driver.HasAvailabilityFor(Window(10, 12), []));
    }

    [Fact]
    public void Driver_GetAvailability_ReducedRequestedButNoneLeft_FallsBackToNormal()
    {
        var driver = NewDriver();
        driver.UpdateDrivingLimits(LimitsWithReducedRestsUsed(DrivingLimits.MaxReducedDailyRests));

        var availability = driver.GetAvailability(Day, useReducedDailyRest: true, []);

        Assert.Equal(TimeSpan.FromHours(13), availability.DailyAvailableWindow);
    }

    [Fact]
    public void Driver_GetAvailability_ReducedRequestedAndAllowed_Is15Hours()
    {
        var driver = NewDriver();
        driver.UpdateDrivingLimits(LimitsWithReducedRestsUsed(0));

        var availability = driver.GetAvailability(Day, useReducedDailyRest: true, []);

        Assert.Equal(TimeSpan.FromHours(15), availability.DailyAvailableWindow);
    }

    private static DrivingLimits LimitsWithReducedRestsUsed(int used) => new(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, used, null, RestType.None, null, RestType.None);

    // Mutation testing (SPEC 0099): the guard, not LINQ or a null dereference, rejects a missing argument.
    [Fact]
    public void For_NoCommittedWork_NamesTheArgument() =>
        Assert.Throws<ArgumentNullException>("committedWork", () => DriverAvailability.For(Day, useReducedDailyRest: false, null!));

    [Fact]
    public void Fits_NoTrip_NamesTheArgument() =>
        Assert.Throws<ArgumentNullException>("tripWindow", () => DriverAvailability.For(Day, false, [Window(8, 10)]).Fits(null!));
}
