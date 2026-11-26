using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// Daily rest as a 24h window after the previous rest (CE 561/2006 Art. 4(g), 8.2, 8.4), evaluated over the driver's full
/// calendar so that the result does not depend on allocation order (SPEC 0041).
/// </summary>
[Trait("Regulation", "Art. 8")]
public class DailyRestWindowTests
{
    private const string DailyRest = "CE_561_2006.DailyRest.11hours";
    private const string ReducedLimit = "CE_561_2006.DailyRest.ReducedLimit";
    private static readonly DateTime Day = new(2035, 3, 7, 0, 0, 0, DateTimeKind.Utc);

    private static DrivingPeriod Period(DateTime start, double hours) => new(1, start, start.AddHours(hours));

    // Weekly rest 3 days before, then a short day ending 12:00 the day before (outside the 24h before B).
    private static List<DrivingPeriod> Context() =>
    [
        Period(Day.AddDays(-3).AddHours(8), 2),
        Period(Day.AddDays(-1).AddHours(8), 4),
    ];

    private static ComplianceCheckResult Validate(IEnumerable<DrivingPeriod> history, DrivingPeriod trip) =>
        LegalComplianceEvaluation.Validate(history.ToList(), trip.Start, trip.Duration);

    [Fact]
    public void Validate_SplitShiftAssignedEarlierFirst_IsCompliant()
    {
        var morning = Period(Day.AddHours(7), 4);
        var afternoon = Period(Day.AddHours(15), 4);

        var result = Validate([.. Context(), morning], afternoon);

        Assert.True(result.IsCompliant, string.Join("; ", result.Violations.Select(v => v.Message)));
    }

    [Fact]
    public void Validate_SplitShiftAssignedLaterFirst_IsCompliant()
    {
        var morning = Period(Day.AddHours(7), 4);
        var afternoon = Period(Day.AddHours(15), 4);

        var result = Validate([.. Context(), afternoon], morning);

        Assert.True(result.IsCompliant, string.Join("; ", result.Violations.Select(v => v.Message)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_SpreadOf16hWithNextDayStartingAt06_ViolatesDailyRestInBothOrders(bool earlierFirst)
    {
        // S = 06:00; breaks of 8h (no daily rest) → the day runs into the next morning, no 9h rest fits in 24h.
        var prev = Period(Day.AddDays(-1).AddHours(14), 4); // ends 18:00 → 12h rest until 06:00
        var morning = Period(Day.AddHours(6), 4);
        var afternoon = Period(Day.AddHours(18), 4);
        var next = Period(Day.AddDays(1).AddHours(6), 2);
        List<DrivingPeriod> history = [Period(Day.AddDays(-3).AddHours(8), 2), prev, next, earlierFirst ? morning : afternoon];

        var result = Validate(history, earlierFirst ? afternoon : morning);

        Assert.Contains(result.Violations, v => v.RuleId == DailyRest);
    }

    [Fact]
    public void Validate_FourReducedDailyRestsBetweenWeeklyRests_ViolatesReducedLimit()
    {
        var (history, trip) = FourDaysEnding20h(withThreeHourBreak: false);

        var result = Validate(history, trip);

        Assert.Contains(result.Violations, v => v.RuleId == ReducedLimit);
        Assert.DoesNotContain(result.Violations, v => v.RuleId == DailyRest);
    }

    [Fact]
    public void Validate_FourSplitDailyRests3hPlus9h_NoDailyRestViolation()
    {
        var (history, trip) = FourDaysEnding20h(withThreeHourBreak: true);

        var result = Validate(history, trip);

        Assert.DoesNotContain(result.Violations, v => v.RuleId is DailyRest or ReducedLimit);
    }

    [Fact]
    public void Validate_ThreeReducedDailyRests_NoReducedLimitViolation()
    {
        var (history, trip) = FourDaysEnding20h(withThreeHourBreak: false, days: 3);

        var result = Validate(history, trip);

        Assert.DoesNotContain(result.Violations, v => v.RuleId is DailyRest or ReducedLimit);
    }

    /// <summary>
    /// Days starting 05:00 and ending 20:00 (15h spread → 9h rest, 11h would not fit).
    /// 9h driving per day; breaks under 3h, or a 3h one when <paramref name="withThreeHourBreak"/>.
    /// The trip is the last block of the last day.
    /// </summary>
    private static (List<DrivingPeriod> History, DrivingPeriod Trip) FourDaysEnding20h(
        bool withThreeHourBreak, int days = 4)
    {
        List<DrivingPeriod> all = [Period(Day.AddDays(-3).AddHours(8), 2)]; // weekly rest before day 0
        for (var d = 0; d < days; d++)
        {
            var dayStart = Day.AddDays(d).AddHours(5);
            if (withThreeHourBreak)
                all.AddRange([Period(dayStart, 3), Period(dayStart.AddHours(6), 2), Period(dayStart.AddHours(10), 2), Period(dayStart.AddHours(13), 2)]); // 05–08, 11–13 (3h break), 15–17, 18–20
            else
                all.AddRange([Period(dayStart, 3), Period(dayStart.AddHours(5), 2), Period(dayStart.AddHours(9), 2), Period(dayStart.AddHours(13), 2)]);  // 05–08, 10–12, 14–16, 18–20
        }

        var trip = all[^1];
        all.RemoveAt(all.Count - 1);
        return (all, trip);
    }
}
