using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>One representative CE 561/2006 scenario per rule; the detail of each rule has its own test class.</summary>
[Trait("Regulation", "Art. 6")]
[Trait("Regulation", "Art. 7")]
[Trait("Regulation", "Art. 8")]
public class LegalComplianceEvaluationSanityTests
{
    [Fact]
    public void FreshDriver_OnlyUnverifiableWeeklyRestWarning_NoDrivingLimitViolations()
    {
        // An empty history cannot prove that a weekly rest is missing, so the only finding is a non-blocking "unverifiable" warning
        // (SPEC 0040 R3).
        var history = Array.Empty<DrivingPeriod>();
        var tripStart = DateTime.UtcNow;
        var tripDuration = TimeSpan.FromHours(4);

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        Assert.True(result.IsCompliant);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("CE_561_2006.WeeklyRest.Unverifiable", violation.RuleId);
    }

    [Fact]
    public void ContinuousDrivingViolation_ExceedsFourPointFiveHours()
    {
        // 4h already driven, 1h more requested = 5h in total.
        var now = DateTime.UtcNow;
        var history = new[]
        {
            new DrivingPeriod(
                driverId: 1,
                start: now.AddHours(-12),
                end: now.AddHours(-8)), // 11h ago, 4h drive
            new DrivingPeriod(
                driverId: 1,
                start: now.AddMinutes(-30),
                end: now) // just finished 30min drive
        };
        var tripStart = now;
        var tripDuration = TimeSpan.FromHours(4.5); // total would be 5h continuous

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        Assert.False(result.IsCompliant);
        Assert.NotEmpty(result.Violations);
        var continuousViolation = result.Violations.FirstOrDefault(v => v.RuleId == "CE_561_2006.ContinuousDriving.4_5hours");
        Assert.NotNull(continuousViolation);
    }

    [Fact]
    public void ContinuousDrivingReset_AfterMandatoryBreak()
    {
        // The 11h rest resets the Art. 7 continuous-driving counter (at least 45min) AND satisfies the separate Art. 8 daily rest (at
        // least 11h): a 50min break alone would reset continuous driving but leave the trip non-compliant on daily rest, which is an
        // independent rule. An older, unrelated stint gives a weekly rest of at least 45h, to isolate this interaction.
        var baseTime = DateTime.UtcNow.Date.AddHours(10);
        var history = new[]
        {
            new DrivingPeriod(
                driverId: 1,
                start: baseTime.AddDays(-10),
                end: baseTime.AddDays(-10).AddHours(1)), // Old, unrelated stint: establishes the weekly rest.
            new DrivingPeriod(
                driverId: 1,
                start: baseTime,
                end: baseTime.AddHours(4)), // 4h of driving, 10:00–14:00.
        };
        var tripStart = baseTime.AddHours(4).AddHours(11); // 11h of rest after the 4h drive.
        var tripDuration = TimeSpan.FromHours(4);

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        Assert.True(result.IsCompliant, "Rest of 11h should reset continuous driving counter and satisfy daily rest");
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void WeeklyDrivingViolation_ExceedsFiftySixHours()
    {
        // 7 days of 8h of driving (56h in total) within the same Monday-to-Sunday week, each day well rested (a gap of at least 11h)
        // and under the 9h daily ceiling, so that only the weekly rule (56h) is exercised. The trip is on the seventh day (Sunday), 12h
        // after that day's driving ends.
        var weekStart = GetWeekStart(DateTime.UtcNow);
        var history = Enumerable.Range(0, 7)
            .Select(day => new DrivingPeriod(
                driverId: 1,
                start: weekStart.AddHours(day * 24),
                end: weekStart.AddHours(day * 24 + 8)))
            .ToArray();
        var tripStart = weekStart.AddHours(6 * 24 + 20); // Sunday 20:00, 12h after the last block ends.
        var tripDuration = TimeSpan.FromHours(1);

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        Assert.False(result.IsCompliant);
        var weeklyViolation = result.Violations.FirstOrDefault(v => v.RuleId == "CE_561_2006.WeeklyDriving.56hours");
        Assert.NotNull(weeklyViolation);
    }

    [Fact]
    public void DailyRestViolation_InsufficientRestInLast24Hours()
    {
        // Never 9h without driving: no daily rest (9h reduced, at least) fits in 24h (SPEC 0041). An older, unrelated stint gives a
        // weekly rest of at least 45h, to isolate the daily-rest violation.
        var now = DateTime.UtcNow;
        var history = new[]
        {
            new DrivingPeriod(
                driverId: 1,
                start: now.AddDays(-10),
                end: now.AddDays(-10).AddHours(1)), // Old, unrelated stint: establishes the weekly rest.
            // 1h stints with gaps below the 9h reduced rest: 5h driven in the working day (Art. 4(k)), but a 26h spread.
            new DrivingPeriod(driverId: 1, start: now.AddHours(-24), end: now.AddHours(-23)),
            new DrivingPeriod(driverId: 1, start: now.AddHours(-16), end: now.AddHours(-15)),
            new DrivingPeriod(driverId: 1, start: now.AddHours(-8), end: now.AddHours(-7))
        };
        var tripStart = now;
        var tripDuration = TimeSpan.FromHours(2);

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        Assert.False(result.IsCompliant);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("CE_561_2006.DailyRest.11hours", violation.RuleId);
    }

    [Fact]
    public void MultipleViolations_AccumulateCorrectly()
    {
        var now = DateTime.UtcNow;
        var weekStart = GetWeekStart(now);
        var history = new[]
        {
            new DrivingPeriod(
                driverId: 1,
                start: weekStart,
                end: weekStart.AddHours(56)),
            new DrivingPeriod(
                driverId: 1,
                start: now.AddHours(-4),
                end: now)
        };
        var tripStart = now;
        var tripDuration = TimeSpan.FromHours(2);

        var result = LegalComplianceEvaluation.Validate(history, tripStart, tripDuration);

        // Continuous and weekly driving, and maybe daily driving.
        Assert.False(result.IsCompliant);
        Assert.NotEmpty(result.Violations);
        // Early in the week the short history may add the non-blocking "WeeklyRest.Unverifiable" warning (SPEC 0040); every other
        // finding must be critical.
        Assert.All(
            result.Violations.Where(v => v.RuleId != "CE_561_2006.WeeklyRest.Unverifiable"),
            v => Assert.Equal(ViolationSeverity.Critical, v.Severity));
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-diff);
    }
}
