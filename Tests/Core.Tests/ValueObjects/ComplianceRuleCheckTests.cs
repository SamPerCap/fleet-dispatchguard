using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// One <see cref="ComplianceRuleCheck"/> per rule, passed or not, with the figures computed and the limit taken from
/// <see cref="DrivingLimits"/> (SPEC 0042 R1).
/// </summary>
public class ComplianceRuleCheckTests
{
    // Monday 2026-11-16: 06:00–10:00 and 10:45–12:45 driven (6h), then a 75-min break before the trip.
    private static readonly DateTime Day = new(2026, 11, 16);
    private static readonly DrivingPeriod[] SixHoursToday =
    [
        new(1, Day.AddHours(6), Day.AddHours(10)),
        new(1, Day.AddHours(10.75), Day.AddHours(12.75))
    ];
    private static readonly DateTime TripStart = Day.AddHours(14);

    private static ComplianceRuleCheck Check(ComplianceCheckResult result, string ruleId) =>
        Assert.Single(result.RuleChecks, c => c.RuleId == ruleId);

    [Fact]
    public void Validate_AnyTrip_RecordsEveryRuleWithItsArticle()
    {
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(2));

        Assert.Equal(
            new[]
            {
                ("CE_561_2006.ContinuousDriving.4_5hours", "Art. 7"),
                ("CE_561_2006.DailyDriving.9hours", "Art. 6.1"),
                ("CE_561_2006.WeeklyDriving.56hours", "Art. 6.2"),
                ("CE_561_2006.BiWeeklyDriving.90hours", "Art. 6.3"),
                ("CE_561_2006.DailyRest.11hours", "Art. 8.2"),
                ("CE_561_2006.DailyRest.ReducedLimit", "Art. 8.4"),
                ("CE_561_2006.WeeklyRest.45hours", "Art. 8.6")
            },
            result.RuleChecks.Select(c => (c.RuleId, c.Article)).ToArray());
    }

    [Fact]
    public void Validate_DailyDrivingExactlyAtLimit_PassedWithComputedFigures()
    {
        // 6h + 4h = 10h: the extended day is still available this week, so the limit applied is 10h.
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(4));

        var daily = Check(result, "CE_561_2006.DailyDriving.9hours");
        Assert.Equal(RuleCheckOutcome.Passed, daily.Outcome);
        Assert.Equal(10, daily.Actual);
        Assert.Equal(DrivingLimits.MaxDailyDrivingExtended.TotalHours, daily.Limit);
        Assert.Equal(RuleUnit.Hours, daily.Unit);
    }

    [Fact]
    public void Validate_DailyDrivingOneMinuteOver_Violated()
    {
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1));

        var daily = Check(result, "CE_561_2006.DailyDriving.9hours");
        Assert.Equal(RuleCheckOutcome.Violated, daily.Outcome);
        Assert.True(daily.Actual > daily.Limit);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public void Validate_ContinuousAfterBreak_ProjectsOnlyTheTrip()
    {
        // The 75-min gap before the trip is a full 45-min break: continuous driving restarts at 0.
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(2));

        var continuous = Check(result, "CE_561_2006.ContinuousDriving.4_5hours");
        Assert.Equal(RuleCheckOutcome.Passed, continuous.Outcome);
        Assert.Equal(2, continuous.Actual);
        Assert.Equal(DrivingLimits.MaxContinuousDriving.TotalHours, continuous.Limit);
    }

    [Fact]
    public void Validate_WeeklyAndBiWeekly_UseDrivingLimitsMaxima()
    {
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(2));

        var weekly = Check(result, "CE_561_2006.WeeklyDriving.56hours");
        Assert.Equal(8, weekly.Actual);
        Assert.Equal(DrivingLimits.MaxWeeklyDriving.TotalHours, weekly.Limit);
        Assert.Equal(DrivingLimits.MaxBiWeeklyDriving.TotalHours, Check(result, "CE_561_2006.BiWeeklyDriving.90hours").Limit);
    }

    [Fact]
    public void Validate_ShortHistory_WeeklyRestIsWarningAndDailyRestPasses()
    {
        var result = LegalComplianceEvaluation.Validate(SixHoursToday, TripStart, TimeSpan.FromHours(2));

        Assert.Equal(RuleCheckOutcome.Warning, Check(result, "CE_561_2006.WeeklyRest.45hours").Outcome);
        var dailyRest = Check(result, "CE_561_2006.DailyRest.11hours");
        Assert.Equal(RuleCheckOutcome.Passed, dailyRest.Outcome);
        // Day 06:00–16:00, window ends 06:00 next day → 14h of rest fit.
        Assert.Equal(14, dailyRest.Actual);
        Assert.Equal(DrivingLimits.DailyRestNormal.TotalHours, dailyRest.Limit);
    }
}
