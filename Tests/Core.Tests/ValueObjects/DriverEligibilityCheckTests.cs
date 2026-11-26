using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>The one criterion for whether a driver can be put on a service at all (SPEC 0066 R1, R2).</summary>
public class DriverEligibilityCheckTests
{
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeWindow Service = new(Day.AddHours(10), Day.AddHours(12));

    private static Driver NewDriver() => new("12345678Z", "Ana", "Gil");

    private static Driver Absent(DateTime start, DateTime end, UnavailabilityType type = UnavailabilityType.SickLeave)
    {
        var driver = NewDriver();
        driver.AddUnavailability(new DriverUnavailability(1, new TimeWindow(start, end), type));
        return driver;
    }

    [Theory]
    [InlineData(480, 600, true)]   // 08:00–10:00: ends exactly at the departure
    [InlineData(480, 601, false)]  // one minute into the service
    [InlineData(720, 840, true)]   // 12:00–14:00: starts exactly at the arrival
    [InlineData(719, 840, false)]  // one minute before the arrival
    [InlineData(630, 660, false)]  // falls in the middle
    [InlineData(600, 720, false)]  // exactly the service
    [InlineData(0, 1440, false)]   // the whole day
    [InlineData(0, 300, true)]     // long before
    [InlineData(900, 1440, true)]  // long after
    [InlineData(-14400, 14400, false)] // weeks around
    public void For_AbsenceAgainstTheService_CountsOnlyIfItOverlaps(int startMinute, int endMinute, bool eligible)
    {
        var driver = Absent(Day.AddMinutes(startMinute), Day.AddMinutes(endMinute));

        Assert.Equal(eligible, DriverEligibilityCheck.For(driver, Service).IsEligible);
        // The same answer as the question the rest of the domain asks (availability of the day, resources of the Control Center).
        Assert.Equal(eligible, driver.IsAvailableDuring(Service));
    }

    [Fact]
    public void For_OneSecond_IsEnough()
    {
        Assert.False(DriverEligibilityCheck.For(Absent(Day, Service.Start.AddSeconds(1)), Service).IsEligible);
        Assert.False(DriverEligibilityCheck.For(Absent(Service.End.AddSeconds(-1), Day.AddDays(1)), Service).IsEligible);
    }

    [Theory]
    [InlineData(UnavailabilityType.Vacation, "vacaciones")]
    [InlineData(UnavailabilityType.SickLeave, "baja médica")]
    [InlineData(UnavailabilityType.WeeklyRest, "descanso semanal")]
    [InlineData(UnavailabilityType.Training, "formación")]
    [InlineData(UnavailabilityType.PersonalLeave, "asuntos propios")]
    [InlineData(UnavailabilityType.Other, "otra indisponibilidad")]
    public void For_EveryType_IsRejectedWithItsNameAndMadridTimes(UnavailabilityType type, string name)
    {
        var violation = Assert.Single(DriverEligibilityCheck.For(Absent(Day.AddHours(7), Day.AddHours(23), type), Service).Violations);

        Assert.Equal($"DriverEligibility.Unavailable.{type}", violation.RuleId);
        Assert.Equal(ViolationSeverity.Critical, violation.Severity);
        Assert.Equal($"Conductor no disponible ({name}) del 23/11/2026 08:00 al 24/11/2026 00:00", violation.Message.Es());
    }

    [Fact]
    public void EveryType_IsCovered() => Assert.Equal(6, Enum.GetValues<UnavailabilityType>().Length);

    [Fact]
    public void For_Inactive_IsRejected_AndReactivatedIsNot()
    {
        var driver = NewDriver();
        driver.Deactivate();
        var violation = Assert.Single(DriverEligibilityCheck.For(driver, Service).Violations);
        Assert.Equal(("DriverEligibility.NotActive", "Conductor no activo"), (violation.RuleId, violation.Message.Es()));

        driver.Reactivate();
        Assert.True(DriverEligibilityCheck.For(driver, Service).IsEligible);
    }

    [Fact]
    public void For_InactiveAndAbsentTwice_ListsEveryReasonInOrder()
    {
        var driver = NewDriver();
        driver.AddUnavailability(new DriverUnavailability(1, new TimeWindow(Day.AddHours(11), Day.AddHours(13)), UnavailabilityType.Training));
        driver.AddUnavailability(new DriverUnavailability(1, new TimeWindow(Day.AddHours(9), Day.AddHours(11)), UnavailabilityType.Vacation));
        driver.AddUnavailability(new DriverUnavailability(1, new TimeWindow(Day.AddHours(20), Day.AddHours(21)), UnavailabilityType.Other));
        driver.Deactivate();

        Assert.Equal(["DriverEligibility.NotActive", "DriverEligibility.Unavailable.Vacation", "DriverEligibility.Unavailable.Training"],
            DriverEligibilityCheck.For(driver, Service).Violations.Select(v => v.RuleId));
    }

    [Fact]
    public void For_EndedAbsence_IsEligibleAgain()
    {
        var driver = Absent(Day, Day.AddDays(1));
        driver.RemoveUnavailability(driver.Unavailabilities.Single());

        Assert.True(DriverEligibilityCheck.For(driver, Service).IsEligible);
    }

    [Fact]
    public void For_ExternalOrNotAutoAssignable_IsEligible() =>
        Assert.True(DriverEligibilityCheck.For(new Driver("1", "Ana", "Gil", isExternal: true, allowsAutoAssignment: false), Service).IsEligible);

    /// <summary>Autumn change (25/10/2026): 02:30 happens twice in Madrid; only the instant counts.</summary>
    [Theory]
    [InlineData(30, true)]   // absent until 00:30 UTC = the first 02:30; service at 01:30 UTC = the second 02:30
    [InlineData(90, true)]   // until 01:30 UTC = exactly the departure
    [InlineData(91, false)]
    public void For_RepeatedHour_ComparesInstants(int endMinuteUtc, bool eligible)
    {
        var night = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var service = new TimeWindow(night.AddMinutes(90), night.AddMinutes(150));

        Assert.Equal(eligible, DriverEligibilityCheck.For(Absent(night.AddDays(-1), night.AddMinutes(endMinuteUtc)), service).IsEligible);
    }

    /// <summary>Spring change (28/03/2027): the Madrid day is 23 h; a leave of that whole local day covers no more than that.</summary>
    [Fact]
    public void For_ShortDay_LeaveOfTheLocalDayEndsAtLocalMidnight()
    {
        var (from, to) = (new DateTime(2027, 3, 27, 23, 0, 0, DateTimeKind.Utc), new DateTime(2027, 3, 28, 22, 0, 0, DateTimeKind.Utc));
        var driver = Absent(from, to);

        Assert.False(DriverEligibilityCheck.For(driver, new TimeWindow(to.AddMinutes(-1), to.AddHours(1))).IsEligible);
        Assert.True(DriverEligibilityCheck.For(driver, new TimeWindow(to, to.AddHours(1))).IsEligible);
        Assert.True(DriverEligibilityCheck.For(driver, new TimeWindow(from.AddHours(-1), from)).IsEligible);
        Assert.Contains("del 28/03/2027 00:00 al 29/03/2027 00:00", DriverEligibilityCheck.For(driver, new TimeWindow(from, to)).Violations[0].Message.Es());
    }
}
