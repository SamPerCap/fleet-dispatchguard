using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// CE 561/2006 Art. 8: a weekly rest is inferred as a gap of at least 24h between consecutive <see cref="DrivingPeriod"/>s before
/// the trip, and its absence is a "CE_561_2006.WeeklyRest.45hours" violation (SPEC 0015).
/// </summary>
[Trait("Regulation", "Art. 8")]
public class WeeklyRestEnforcementTests
{
    private const string WeeklyRestRuleId = "CE_561_2006.WeeklyRest.45hours";

    private static bool HasWeeklyRestViolation(ComplianceCheckResult result) =>
        result.Violations.Any(v => v.RuleId == WeeklyRestRuleId);

    [Fact]
    public void Validate_NoGapOfTwentyFourHoursInLastTwoWeeks_IncludesWeeklyRestViolation()
    {
        // Works every day with only short daily rests (e.g. 10h), never a gap wide enough for a weekly rest.
        var tripStart = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);
        var history = new List<DrivingPeriod>();

        for (var day = 13; day >= 1; day--)
        {
            var start = tripStart.AddDays(-day).AddHours(6);
            var end = start.AddHours(8);
            history.Add(new DrivingPeriod(1, start, end));
        }

        var result = LegalComplianceEvaluation.Validate(history, tripStart, TimeSpan.FromHours(1));

        Assert.False(result.IsCompliant);
        Assert.True(HasWeeklyRestViolation(result));
    }

    [Fact]
    public void Validate_FortySixHourGapBeforeTrip_DoesNotIncludeWeeklyRestViolation()
    {
        // A 46h gap (normal weekly rest); the trip starts right when the rest ends.
        var restEnd = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);
        var tripStart = restEnd.AddHours(1);

        var beforeRestEnd = restEnd - TimeSpan.FromHours(46);
        var beforeRestStart = beforeRestEnd.AddHours(-4);

        var history = new List<DrivingPeriod>
        {
            new(1, beforeRestStart, beforeRestEnd),
            new(1, restEnd, restEnd.AddHours(1)),
        };

        var result = LegalComplianceEvaluation.Validate(history, tripStart, TimeSpan.FromHours(1));

        Assert.False(HasWeeklyRestViolation(result));
    }

    [Fact]
    public void Validate_ThirtyHourGapLessThanSixDaysAgo_DoesNotIncludeWeeklyRestViolation()
    {
        // Reduced weekly rest of 30h, 3 days before the trip.
        var tripStart = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);
        var restEnd = tripStart.AddDays(-3);
        var restStart = restEnd - TimeSpan.FromHours(30);
        var beforeRestStart = restStart.AddHours(-4);

        var history = new List<DrivingPeriod>
        {
            new(1, beforeRestStart, restStart),
            new(1, restEnd, restEnd.AddHours(2)),
        };

        var result = LegalComplianceEvaluation.Validate(history, tripStart, TimeSpan.FromHours(1));

        Assert.False(HasWeeklyRestViolation(result));
    }

    [Fact]
    public void Validate_EmptyHistory_UnverifiableWarningInsteadOfViolation_Spec0040()
    {
        // An empty history cannot prove that a weekly rest is missing (SPEC 0040 R3).
        var tripStart = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);
        var history = new List<DrivingPeriod>();

        var result = LegalComplianceEvaluation.Validate(history, tripStart, TimeSpan.FromHours(1));

        Assert.True(result.IsCompliant);
        Assert.False(HasWeeklyRestViolation(result));
        Assert.Contains(result.Violations, v => v.RuleId == "CE_561_2006.WeeklyRest.Unverifiable");
    }

    [Fact]
    public void Validate_SameHistoryDifferentTripStart_ProducesDifferentComplianceOutcome()
    {
        // A single gap of 40h (reduced weekly rest) anchored at a fixed instant.
        var restEnd = new DateTime(2026, 6, 20, 6, 0, 0, DateTimeKind.Utc);
        var restStart = restEnd - TimeSpan.FromHours(40);
        var beforeRestStart = restStart.AddHours(-4);

        var history = new List<DrivingPeriod>
        {
            new(1, beforeRestStart, restStart),
            new(1, restEnd, restEnd.AddHours(2)),
        };

        // Shortly after the rest: still within the 6 days.
        var tripStartNearRest = restEnd.AddDays(2);
        var resultNearRest = LegalComplianceEvaluation.Validate(history, tripStartNearRest, TimeSpan.FromHours(1));

        // More than 6 days after the gap: a violation. Daily work up to 12h before the trip, so that the time just before it is not itself
        // a weekly rest (the trailing gap counts when it reaches 24h, SPEC 0039).
        var tripStartFarFromRest = restEnd.AddDays(8);
        var busyHistory = new List<DrivingPeriod>(history);
        for (var day = 1; day <= 7; day++)
            busyHistory.Add(new(1, restEnd.AddDays(day).AddHours(8), restEnd.AddDays(day).AddHours(12)));
        var resultFarFromRest = LegalComplianceEvaluation.Validate(busyHistory, tripStartFarFromRest, TimeSpan.FromHours(1));

        Assert.False(HasWeeklyRestViolation(resultNearRest));
        Assert.True(HasWeeklyRestViolation(resultFarFromRest));
    }

    [Fact]
    public void Validate_FullyCompliantHistoryWithRecentWeeklyRest_ProducesNoViolations()
    {
        // Light, well-rested history that ends in a 48h weekly rest 2 days before the trip, so that every other rule (continuous, daily,
        // weekly, bi-weekly, daily rest) is comfortably met alongside the weekly rest.
        var tripStart = new DateTime(2026, 7, 2, 8, 0, 0, DateTimeKind.Utc);

        var weeklyRestEnd = tripStart.AddDays(-2);
        var weeklyRestStart = weeklyRestEnd - TimeSpan.FromHours(48);

        var history = new List<DrivingPeriod>
        {
            new(1, weeklyRestStart.AddHours(-2), weeklyRestStart),
            new(1, weeklyRestEnd, weeklyRestEnd.AddHours(2)),
        };

        var result = LegalComplianceEvaluation.Validate(history, tripStart, TimeSpan.FromHours(1));

        Assert.True(result.IsCompliant);
        Assert.Empty(result.Violations);
    }

    // The rest between the last driving period and the trip counts as a weekly rest when it reaches 24h (SPEC 0039).

    private static List<DrivingPeriod> SixDaysEndingAt(DateTime lastEnd)
    {
        // 6 consecutive days of 8h with 16h daily rests: no weekly rest inside the history, which covers the whole 6×24h window, so its
        // absence is provable and not "unverifiable".
        var history = new List<DrivingPeriod>();
        for (var d = 7; d >= 0; d--)
        {
            var end = lastEnd.AddDays(-d);
            history.Add(new DrivingPeriod(1, end.AddHours(-8), end));
        }
        return history;
    }

    [Fact]
    public void Validate_GapOfExactly24hBetweenLastPeriodAndTrip_NoWeeklyRestViolation()
    {
        var lastEnd = new DateTime(2026, 7, 10, 16, 0, 0, DateTimeKind.Utc);
        var history = SixDaysEndingAt(lastEnd);

        var result = LegalComplianceEvaluation.Validate(
            history, lastEnd + DrivingLimits.WeeklyRestReduced, TimeSpan.FromHours(1));

        Assert.False(HasWeeklyRestViolation(result));
    }

    [Fact]
    public void Validate_GapOf23h59BetweenLastPeriodAndTrip_IncludesWeeklyRestViolation()
    {
        var lastEnd = new DateTime(2026, 7, 10, 16, 0, 0, DateTimeKind.Utc);
        var history = SixDaysEndingAt(lastEnd);

        var result = LegalComplianceEvaluation.Validate(
            history, lastEnd + DrivingLimits.WeeklyRestReduced - TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

        Assert.True(HasWeeklyRestViolation(result));
    }

    // Mutation testing (SPEC 0099): a gap of exactly 24 h between two periods is a weekly rest; one minute less is not.
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public void Validate_GapBetweenPeriodsAroundTwentyFourHours_DecidesTheWeeklyRest(int minutes, bool violation)
    {
        var dayZero = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        var afterRest = dayZero.AddHours(2) + DrivingLimits.WeeklyRestReduced + TimeSpan.FromMinutes(minutes);
        var history = new List<DrivingPeriod> { new(1, dayZero, dayZero.AddHours(2)), new(1, afterRest, afterRest.AddHours(2)) };
        history.AddRange(Enumerable.Range(2, 5).Select(day => new DrivingPeriod(1, dayZero.AddDays(day), dayZero.AddDays(day).AddHours(2))));

        var result = LegalComplianceEvaluation.Validate(history, dayZero.AddDays(7), TimeSpan.FromHours(2));

        Assert.Equal(violation, HasWeeklyRestViolation(result));
    }
}
