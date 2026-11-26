using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// <see cref="DrivingLimits"/> (accumulated driving, remaining time and rests of CE 561/2006) and <see cref="LegalValidation"/>,
/// the immutable snapshot attached to a trip.
/// </summary>
[Trait("Regulation", "Art. 6")]
[Trait("Regulation", "Art. 7")]
[Trait("Regulation", "Art. 8")]
public class LegalValidationTests
{
    // These scenarios exercise the daily budget (CanDrive, AvailableToday); the Art. 7 counter has its own tests below.

    [Fact]
    public void ContinuousDriving_FourPointFiveHourTrip_WithFreshDay_IsCompliant()
    {
        // With no extended days used this week, AvailableToday() reports against the 10h extended ceiling, not the 9h one.
        var limits = DrivingLimits.Empty();
        var continuousTrip = TimeSpan.FromHours(4.5);

        var canDrive = limits.CanDrive(continuousTrip);

        Assert.True(canDrive);
        Assert.Equal(TimeSpan.FromHours(10), limits.AvailableToday());
    }

    [Fact]
    public void ContinuousDriving_ExceedsRemainingDailyBudget_IsNonCompliant()
    {
        // Already drove 8h today and has exhausted the extended-day exception (2 used this week): the 9h ceiling leaves 1h.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(8),
            drivingThisWeek: TimeSpan.FromHours(8),
            drivingLastTwoWeeks: TimeSpan.FromHours(8),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.Date.AddHours(-13),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var continuousTrip = TimeSpan.FromHours(4.5); // It would push the total to 12.5h today.

        var canDrive = limits.CanDrive(continuousTrip);

        Assert.False(canDrive);
        Assert.Equal(TimeSpan.FromHours(1), limits.AvailableToday());
    }

    [Fact]
    public void ContinuousDriving_AfterRestBreak_BudgetIsResetForNewDay()
    {
        // At the extended-day limit (10h, exception exhausted); the snapshot after the rest shows the counter reset.
        var beforeRest = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(10),
            drivingThisWeek: TimeSpan.FromHours(20),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.Zero, beforeRest.AvailableToday());

        var afterRest = new DrivingLimits(
            drivingToday: TimeSpan.Zero,
            drivingThisWeek: TimeSpan.FromHours(20),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow,
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.FromHours(9), afterRest.AvailableToday());
        Assert.True(afterRest.CanDrive(TimeSpan.FromHours(4.5)));
    }

    [Fact]
    public void ContinuousDriving_MultiplePeriods_AccumulateAgainstDailyBudget()
    {
        // Two periods today that add up to 5h. Extended-day exception exhausted, so the normal 9h ceiling applies.
        var firstPeriod = TimeSpan.FromHours(2.5);
        var secondPeriod = TimeSpan.FromHours(2.5);
        var accumulatedToday = firstPeriod + secondPeriod;

        var limits = new DrivingLimits(
            drivingToday: accumulatedToday,
            drivingThisWeek: accumulatedToday,
            drivingLastTwoWeeks: accumulatedToday,
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.Date.AddHours(-13),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        // 4h left of the 9h budget.
        Assert.Equal(TimeSpan.FromHours(5), limits.DrivingToday);
        Assert.Equal(TimeSpan.FromHours(4), limits.AvailableToday());
    }

    [Fact]
    public void DailyDriving_UpToNineHours_IsCompliant()
    {
        // Extended-day exception exhausted (2 used this week), so the normal 9h ceiling applies.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(9),
            drivingLastTwoWeeks: TimeSpan.FromHours(9),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.Zero, limits.AvailableToday());
        Assert.False(limits.CanDrive(TimeSpan.FromMinutes(1)));
        Assert.True(limits.CanDrive(TimeSpan.Zero));
    }

    [Fact]
    public void DailyDriving_ExceedsNineHoursWithoutExtendedException_IsNonCompliant()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(9),
            drivingLastTwoWeeks: TimeSpan.FromHours(9),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var canDriveExtra = limits.CanDrive(TimeSpan.FromHours(0.5));

        Assert.False(canDriveExtra);
    }

    [Fact]
    public void DailyDriving_ExtendedTenHourException_FirstUseThisWeek_IsCompliant()
    {
        // No extended day used so far this week: the exception is available.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(9),
            drivingLastTwoWeeks: TimeSpan.FromHours(9),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.True(limits.CanUseExtendedDay());
        Assert.Equal(TimeSpan.FromHours(1), limits.AvailableToday()); // Up to 10h allowed.
        Assert.True(limits.CanDrive(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void DailyDriving_ExtendedTenHourException_SecondUseThisWeek_IsCompliant()
    {
        // One extended day used: still within the twice a week of Art. 6.1.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(19),
            drivingLastTwoWeeks: TimeSpan.FromHours(19),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 1,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.True(limits.CanUseExtendedDay());
        Assert.Equal(TimeSpan.FromHours(1), limits.AvailableToday());
    }

    [Fact]
    public void DailyDriving_ExtendedTenHourException_ThirdUseThisWeek_IsNonCompliant()
    {
        // Extended day already used twice this week: exception exhausted.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(29),
            drivingLastTwoWeeks: TimeSpan.FromHours(29),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        // Only the normal 9h ceiling applies now, and it has been reached.
        Assert.False(limits.CanUseExtendedDay());
        Assert.Equal(TimeSpan.Zero, limits.AvailableToday());
        Assert.False(limits.CanDrive(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void WeeklyDriving_ExactlyFiftySixHours_IsCompliant_NoRemainingTime()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(8),
            drivingThisWeek: TimeSpan.FromHours(56),
            drivingLastTwoWeeks: TimeSpan.FromHours(56),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.Zero, limits.AvailableThisWeek());
        Assert.False(limits.CanDrive(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void WeeklyDriving_ExceedsFiftySixHours_IsNonCompliant()
    {
        // 56h driven this week (8h × 7 days) and one more 2h trip: the weekly ceiling blocks it although the daily budget would not.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(0),
            drivingThisWeek: TimeSpan.FromHours(56),
            drivingLastTwoWeeks: TimeSpan.FromHours(56),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-13),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var canDrive = limits.CanDrive(TimeSpan.FromHours(2));

        Assert.False(canDrive);
        Assert.Equal(TimeSpan.Zero, limits.AvailableThisWeek());
    }

    [Fact]
    public void WeeklyDriving_RollsOverAtMonday_ResetsToZero()
    {
        // End-of-week snapshot at the weekly ceiling.
        var endOfWeek = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(8),
            drivingThisWeek: TimeSpan.FromHours(56),
            drivingLastTwoWeeks: TimeSpan.FromHours(100),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.Zero, endOfWeek.AvailableThisWeek());

        // New week, after Monday 00:00.
        var newWeek = new DrivingLimits(
            drivingToday: TimeSpan.Zero,
            drivingThisWeek: TimeSpan.Zero,
            drivingLastTwoWeeks: TimeSpan.FromHours(56), // Last week still counts toward the bi-weekly cap.
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow,
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow,
            lastWeeklyRestType: RestType.WeeklyNormal);

        Assert.Equal(TimeSpan.FromHours(56), newWeek.AvailableThisWeek());
        Assert.Equal(0, newWeek.ExtendedDaysUsedThisWeek);
    }

    [Fact]
    public void DailyRest_ElevenHoursSinceLastRest_DoesNotYetRequireRest()
    {
        // A daily rest is needed once 13h have elapsed since the last one: 11h of rest + 13h of availability = 24h cycle.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(4),
            drivingThisWeek: TimeSpan.FromHours(4),
            drivingLastTwoWeeks: TimeSpan.FromHours(4),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-11),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.False(limits.NeedsDailyRest());
    }

    [Fact]
    public void DailyRest_LessThanElevenHours_StillWithinAvailabilityWindow()
    {
        // Only 10h50min since the last daily rest started.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(2),
            drivingLastTwoWeeks: TimeSpan.FromHours(2),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-10).AddMinutes(-50),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        // Under the 13h of availability: the rest is not mandatory yet.
        Assert.False(limits.NeedsDailyRest());
    }

    [Fact]
    public void DailyRest_NoRestRecordedInLast24h_RequiresRestImmediately()
    {
        // No daily rest has ever been recorded.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.Zero,
            drivingThisWeek: TimeSpan.Zero,
            drivingLastTwoWeeks: TimeSpan.Zero,
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.True(limits.NeedsDailyRest());
    }

    [Fact]
    public void DailyRest_ThirteenOrMoreHoursSinceLastRest_RequiresRest()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(9),
            drivingLastTwoWeeks: TimeSpan.FromHours(9),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-13),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.True(limits.NeedsDailyRest());
    }

    [Fact]
    public void WeeklyRest_SixOrMoreDaysSinceLastWeeklyRest_RequiresWeeklyRest()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(1),
            drivingThisWeek: TimeSpan.FromHours(40),
            drivingLastTwoWeeks: TimeSpan.FromHours(80),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-12),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-6),
            lastWeeklyRestType: RestType.WeeklyNormal);

        Assert.True(limits.NeedsWeeklyRest(DateTime.UtcNow));
    }

    [Fact]
    public void WeeklyRest_NeverTaken_NeedsWeeklyRestFlag_VerifiabilityDecidedByEvaluation()
    {
        // The flag only says "no known weekly rest"; whether that blocks (R2) or warns (R3) is decided in LegalComplianceEvaluation from
        // how much history is known (SPEC 0040).
        var limits = DrivingLimits.Empty();

        Assert.True(limits.NeedsWeeklyRest(DateTime.UtcNow));
    }

    [Fact]
    public void RemainingDaily_DrivenSevenOfNineHours_RemainingIsTwoHours()
    {
        // Extended-day exception exhausted (2 used this week), so the normal 9h ceiling applies.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(7),
            drivingThisWeek: TimeSpan.FromHours(7),
            drivingLastTwoWeeks: TimeSpan.FromHours(7),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.FromHours(2), limits.AvailableToday());
    }

    [Fact]
    public void RemainingDaily_DrivenEightPointFiveOfNineHours_RemainingIsThirtyMinutes()
    {
        // Extended-day exception exhausted (2 used this week), so the normal 9h ceiling applies.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(8.5),
            drivingThisWeek: TimeSpan.FromHours(8.5),
            drivingLastTwoWeeks: TimeSpan.FromHours(8.5),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.FromMinutes(30), limits.AvailableToday());
    }

    [Fact]
    public void RemainingWeekly_DrivenFiftyOfFiftySixHours_RemainingIsSixHours()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(8),
            drivingThisWeek: TimeSpan.FromHours(50),
            drivingLastTwoWeeks: TimeSpan.FromHours(50),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.FromHours(6), limits.AvailableThisWeek());
    }

    [Fact]
    public void RemainingBiWeekly_DrivenEightyOfNinetyHours_RemainingIsTenHours()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(30),
            drivingLastTwoWeeks: TimeSpan.FromHours(80),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.Equal(TimeSpan.FromHours(10), limits.AvailableInTwoWeeks());
    }

    [Fact]
    public void RemainingTime_NeverGoesNegative_WhenOverLimit()
    {
        // Over the daily and weekly limits, e.g. after a manual override or a data correction.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(11),
            drivingThisWeek: TimeSpan.FromHours(60),
            drivingLastTwoWeeks: TimeSpan.FromHours(95),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        // Remaining times clamp at zero, never negative.
        Assert.Equal(TimeSpan.Zero, limits.AvailableToday());
        Assert.Equal(TimeSpan.Zero, limits.AvailableThisWeek());
        Assert.Equal(TimeSpan.Zero, limits.AvailableInTwoWeeks());
    }

    [Fact]
    public void MultipleViolations_AllReportedSimultaneously()
    {
        var validation = LegalValidation.NonCompliant(
            Texts.AsWritten("CE_561_2006.DailyDriving.9hours: Daily driving would be 11h (maximum 9h)"),
            Texts.AsWritten("CE_561_2006.WeeklyDriving.56hours: Weekly driving would be 58h (maximum 56h)"),
            Texts.AsWritten("CE_561_2006.DailyRest.11hours: Only 9h of rest recorded in last 24h"));

        Assert.False(validation.IsCompliant);
        Assert.Equal(3, validation.Violations.Count);
        Assert.Contains(validation.Violations, v => v.Es().Contains("DailyDriving"));
        Assert.Contains(validation.Violations, v => v.Es().Contains("WeeklyDriving"));
        Assert.Contains(validation.Violations, v => v.Es().Contains("DailyRest"));
    }

    [Fact]
    public void MultipleViolations_CombinedWithDrivingLimitsState_DailyAndWeeklyBothExceeded()
    {
        // Daily and weekly budgets exhausted at once.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(9),
            drivingThisWeek: TimeSpan.FromHours(56),
            drivingLastTwoWeeks: TimeSpan.FromHours(56),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-14),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var violations = new List<string>();
        if (limits.AvailableToday() == TimeSpan.Zero)
        {
            violations.Add("CE_561_2006.DailyDriving.9hours");
        }
        if (limits.AvailableThisWeek() == TimeSpan.Zero)
        {
            violations.Add("CE_561_2006.WeeklyDriving.56hours");
        }
        if (limits.NeedsDailyRest())
        {
            violations.Add("CE_561_2006.DailyRest.11hours");
        }

        var validation = LegalValidation.NonCompliant(violations.Select(Texts.AsWritten).ToArray());

        Assert.False(validation.IsCompliant);
        Assert.Equal(3, validation.Violations.Count);
    }

    [Fact]
    public void CompliantWithWarnings_IsStillCompliant_ButWarningsArePreserved()
    {
        // E.g. approaching the weekly limit, but not exceeding it yet.
        var validation = LegalValidation.CompliantWithWarnings(
            Texts.AsWritten("Approaching weekly driving limit: 52h of 56h used"));

        Assert.True(validation.IsCompliant);
        Assert.Empty(validation.Violations);
        Assert.Single(validation.Warnings);
    }

    [Fact]
    public void LegalValidation_Compliant_HasNoViolationsOrWarnings()
    {
        var validation = LegalValidation.Compliant();

        Assert.True(validation.IsCompliant);
        Assert.Empty(validation.Violations);
        Assert.Empty(validation.Warnings);
        Assert.True(validation.ValidatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void LegalValidation_PropertiesHaveNoPublicSetters()
    {
        // Reflection guard: the value object cannot be mutated after construction.
        var type = typeof(LegalValidation);
        var publicProperties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotEmpty(publicProperties);
        foreach (var property in publicProperties)
        {
            Assert.False(
                property.CanWrite,
                $"Property '{property.Name}' on LegalValidation must be read-only (no public setter).");
        }
    }

    [Fact]
    public void LegalValidation_ViolationsCollection_IsReadOnlyAndCannotBeCastBackToList()
    {
        var validation = LegalValidation.NonCompliant(Texts.AsWritten("rule-violated"));

        // It must not be a mutable List<T> that callers could downcast and mutate.
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<LocalizedText>>(validation.Violations);
    }

    [Fact]
    public void LegalValidation_MutatingSourceCollectionAfterConstruction_DoesNotAffectValidation()
    {
        var sourceViolations = new List<string> { "initial-violation" };
        var validation = new LegalValidation(false, sourceViolations.Select(Texts.AsWritten), []);

        sourceViolations.Add("added-after-construction");
        sourceViolations.Clear();

        // Defensive copy: the snapshot is unaffected.
        Assert.Single(validation.Violations);
        Assert.Equal("initial-violation", validation.Violations.First().Es());
    }

    [Fact]
    public void DrivingLimits_PropertiesHaveNoPublicSetters()
    {
        var type = typeof(DrivingLimits);
        var publicProperties = type.GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotEmpty(publicProperties);
        foreach (var property in publicProperties)
        {
            Assert.False(
                property.CanWrite,
                $"Property '{property.Name}' on DrivingLimits must be read-only (no public setter).");
        }
    }

    [Fact]
    public void LegalValidation_ParameterlessConstructor_UsedByEfCore_ProducesDefaultState()
    {
        // The parameterless constructor exists only for EF Core; this documents its default state.
        var validation = new LegalValidation();

        Assert.False(validation.IsCompliant);
        Assert.Null(validation.Violations);
        Assert.Null(validation.Warnings);
        Assert.Equal(default, validation.ValidatedAt);
    }

    [Fact]
    public void DrivingLimits_Empty_ProducesZeroedDefensiveSnapshot()
    {
        var limits = DrivingLimits.Empty();

        Assert.Equal(TimeSpan.Zero, limits.DrivingToday);
        Assert.Equal(TimeSpan.Zero, limits.DrivingThisWeek);
        Assert.Equal(TimeSpan.Zero, limits.DrivingLastTwoWeeks);
        Assert.Equal(0, limits.ExtendedDaysUsedThisWeek);
        Assert.Equal(0, limits.ReducedDailyRestsUsed);
        Assert.Null(limits.LastDailyRestDate);
        Assert.Equal(RestType.None, limits.LastDailyRestType);
        Assert.Null(limits.LastWeeklyRestDate);
        Assert.Equal(RestType.None, limits.LastWeeklyRestType);
    }

    [Fact]
    public void DrivingLimits_ToString_ProducesHumanReadableSummary()
    {
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(4),
            drivingThisWeek: TimeSpan.FromHours(20),
            drivingLastTwoWeeks: TimeSpan.FromHours(35),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var summary = limits.ToString();

        // ToString() uses the current culture's decimal separator, and so do the expected fragments.
        Assert.Contains($"Today: {4.0:F1}h", summary);
        Assert.Contains($"Week: {20.0:F1}h", summary);
        Assert.Contains($"BiWeekly: {35.0:F1}h", summary);
    }

    [Fact]
    public void ReducedDailyRest_AllowedUpToThreeTimesBetweenWeeklyRests()
    {
        var limitsWithCapacity = new DrivingLimits(
            drivingToday: TimeSpan.Zero,
            drivingThisWeek: TimeSpan.Zero,
            drivingLastTwoWeeks: TimeSpan.Zero,
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 2,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var limitsExhausted = new DrivingLimits(
            drivingToday: TimeSpan.Zero,
            drivingThisWeek: TimeSpan.Zero,
            drivingLastTwoWeeks: TimeSpan.Zero,
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 3,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        Assert.True(limitsWithCapacity.CanUseReducedDailyRest());
        Assert.False(limitsExhausted.CanUseReducedDailyRest());
    }

    [Theory]
    [InlineData(0, 9, false)] // The daily budget is enough, but one stretch of 9h
                                 // breaks the 4.5h of continuous driving of Art. 7.
    [InlineData(8, 1, true)] // 1h remaining covers a 1h trip exactly.
    [InlineData(8.5, 1, false)] // Only 30min remaining.
    [InlineData(9, 0.01, false)] // At the ceiling, any further driving is too much.
    public void CanDrive_DailyBoundary_MatchesExpectedCompliance(
        double drivenTodayHours, double requestedTripHours, bool expectedCompliant)
    {
        // Extended-day exception exhausted (2 used this week), so the normal 9h ceiling applies.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(drivenTodayHours),
            drivingThisWeek: TimeSpan.FromHours(drivenTodayHours),
            drivingLastTwoWeeks: TimeSpan.FromHours(drivenTodayHours),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 2,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: null,
            lastWeeklyRestType: RestType.None);

        var result = limits.CanDrive(TimeSpan.FromHours(requestedTripHours));

        Assert.Equal(expectedCompliant, result);
    }

    // Art. 7: 4.5h at most and then a 45min break. CanDrive also enforces it, alongside the daily, weekly and bi-weekly ceilings.

    [Fact]
    public void ContinuousDriving_MaxFourPointFive_Compliant()
    {
        // 4h + 30min = exactly 4.5h, which is inclusive (Art. 7).
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(10),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.FromHours(4),
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-5),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-3),
            lastWeeklyRestType: RestType.WeeklyNormal);

        var tripDuration = TimeSpan.FromMinutes(30);

        Assert.True(limits.CanDriveContinuous(tripDuration));
    }

    [Fact]
    public void ContinuousDriving_ExceedsFourPointFive_NonCompliant()
    {
        // 4h + 31min exceeds 4.5h.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(10),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.FromHours(4),
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-5),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-3),
            lastWeeklyRestType: RestType.WeeklyNormal);

        var tripDuration = TimeSpan.FromMinutes(31);

        Assert.False(limits.CanDriveContinuous(tripDuration));
    }

    [Fact]
    public void ContinuousDriving_AfterBreak_Resets()
    {
        // The mandatory break of at least 45min resets DrivingSinceLastBreak and restores the full 4.5h budget.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(10),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.Zero,
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-5),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-3),
            lastWeeklyRestType: RestType.WeeklyNormal);

        var tripDuration = TimeSpan.FromHours(4);

        Assert.True(limits.CanDriveContinuous(tripDuration));
    }

    [Fact]
    public void RemainingContinuousDrivingTime_CalculatedCorrectly()
    {
        // 4.5h − 3h = 1.5h remaining.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(10),
            drivingLastTwoWeeks: TimeSpan.FromHours(20),
            drivingSinceLastBreak: TimeSpan.FromHours(3),
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-5),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-3),
            lastWeeklyRestType: RestType.WeeklyNormal);

        Assert.Equal(TimeSpan.FromHours(1.5), limits.RemainingContinuousDrivingTime);
    }

    [Fact]
    public void CanDrive_RespectsContinuousLimit()
    {
        // Daily, weekly and bi-weekly budgets are within range (2h/9h, 30h/56h, 60h/90h), but DrivingSinceLastBreak is already at the
        // 4.5h ceiling: only the continuous-driving rule blocks.
        var limits = new DrivingLimits(
            drivingToday: TimeSpan.FromHours(2),
            drivingThisWeek: TimeSpan.FromHours(30),
            drivingLastTwoWeeks: TimeSpan.FromHours(60),
            drivingSinceLastBreak: TimeSpan.FromHours(4.5),
            extendedDaysUsedThisWeek: 0,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: DateTime.UtcNow.AddHours(-5),
            lastDailyRestType: RestType.DailyNormal,
            lastWeeklyRestDate: DateTime.UtcNow.AddDays(-3),
            lastWeeklyRestType: RestType.WeeklyNormal);

        var tripDuration = TimeSpan.FromMinutes(15);

        Assert.False(limits.CanDrive(tripDuration));
    }

    // Mutation testing (SPEC 0099): what Validate says is left after the trip, per limit.
    private static readonly DateTime RemainingMonday = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

    private static DrivingPeriod Driving(int day, double fromHour, double toHour) =>
        new(1, RemainingMonday.AddDays(day).AddHours(fromHour), RemainingMonday.AddDays(day).AddHours(toHour));

    [Fact]
    public void Validate_ExtendedDayStillAvailable_RemainingCountsFromTenHours()
    {
        var result = LegalComplianceEvaluation.Validate([Driving(0, 8, 11)], RemainingMonday.AddHours(12), TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromHours(5), result.RemainingToday);
        Assert.Equal(DrivingLimits.MaxWeeklyDriving - TimeSpan.FromHours(5), result.RemainingThisWeek);
        Assert.Equal(DrivingLimits.MaxBiWeeklyDriving - TimeSpan.FromHours(5), result.RemainingLastTwoWeeks);
    }

    [Fact]
    public void Validate_BothExtendedDaysUsed_RemainingCountsFromNineHours()
    {
        List<DrivingPeriod> history =
        [
            Driving(0, 6, 10.5), Driving(0, 11.25, 15.75), Driving(0, 16.5, 17.5),
            Driving(1, 6, 10.5), Driving(1, 11.25, 15.75), Driving(1, 16.5, 17.5),
            Driving(2, 6, 10.5), Driving(2, 11.25, 11.75),
        ];

        var result = LegalComplianceEvaluation.Validate(history, RemainingMonday.AddDays(2).AddHours(12.5), TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromHours(2), result.RemainingToday); // 9 h − 5 h driven − 2 h of the trip
    }

    [Fact]
    public void Validate_TripBeyondTheDailyLimit_RemainingIsZeroNeverNegative()
    {
        var result = LegalComplianceEvaluation.Validate([Driving(0, 6, 10.5), Driving(0, 11.25, 15.75)], RemainingMonday.AddHours(16.5), TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.Zero, result.RemainingToday); // 10 h − 9 h − 2 h, not −1 h
    }

    [Fact]
    public void Validate_NoHistory_NamesTheArgument() =>
        Assert.Throws<ArgumentNullException>("drivingHistory", () => LegalComplianceEvaluation.Validate(null!, RemainingMonday, TimeSpan.FromHours(1)));
}
