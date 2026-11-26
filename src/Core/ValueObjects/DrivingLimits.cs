using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// The only source of the CE 561/2006 figures, and the driving and rests a driver accumulates against them.
/// </summary>
public sealed class DrivingLimits
{
    public static readonly TimeSpan MaxDailyDriving = TimeSpan.FromHours(9);
    public static readonly TimeSpan MaxDailyDrivingExtended = TimeSpan.FromHours(10); // Art. 6.1: at most twice a week.
    public static readonly TimeSpan MaxWeeklyDriving = TimeSpan.FromHours(56);
    public static readonly TimeSpan MaxBiWeeklyDriving = TimeSpan.FromHours(90);

    public static readonly TimeSpan DailyRestNormal = TimeSpan.FromHours(11);
    public static readonly TimeSpan DailyRestReduced = TimeSpan.FromHours(9); // Art. 8.4: at most 3 times between weekly rests.
    public static readonly int MaxReducedDailyRests = 3;
    public static readonly int MaxExtendedDaysPerWeek = 2; // Art. 6.1: days of 10h per week.
    /// <summary>Art. 4(g): first part of a split daily rest; the second part is <see cref="DailyRestReduced"/> (SPEC 0041).</summary>
    public static readonly TimeSpan SplitDailyRestFirstPart = TimeSpan.FromHours(3);
    /// <summary>Art. 8.2: the next daily rest must be completed within 24h of the end of the previous rest (SPEC 0041).</summary>
    public static readonly TimeSpan DailyRestWindow = TimeSpan.FromHours(24);

    public static readonly TimeSpan WeeklyRestNormal = TimeSpan.FromHours(45);
    public static readonly TimeSpan WeeklyRestReduced = TimeSpan.FromHours(24); // Art. 8.6: must be compensated before the end of the third week.
    /// <summary>Art. 8.6: a weekly rest must start no later than the end of 6×24h periods since the previous one.</summary>
    public static readonly TimeSpan MaxIntervalBetweenWeeklyRests = TimeSpan.FromDays(6);

    public static readonly TimeSpan MaxContinuousDriving = TimeSpan.FromHours(4.5);
    public static readonly TimeSpan MandatoryContinuousBreak = TimeSpan.FromMinutes(45);

    /// <summary>The working day between daily rests (Art. 4(k), SPEC 0064), not the calendar day.</summary>
    public TimeSpan DrivingToday { get; }

    /// <summary>Monday to Sunday, the strictest of Madrid time and UTC (SPEC 0064).</summary>
    public TimeSpan DrivingThisWeek { get; }

    /// <summary>The previous and the current natural week (Art. 6.3, SPEC 0064).</summary>
    public TimeSpan DrivingLastTwoWeeks { get; }

    /// <summary>Art. 7: at most 4.5h of continuous driving. Only a break of at least 45 minutes resets it.</summary>
    public TimeSpan DrivingSinceLastBreak { get; }

    /// <summary>Days of more than 9h and up to 10h.</summary>
    public int ExtendedDaysUsedThisWeek { get; }

    /// <summary>Since the last weekly rest.</summary>
    public int ReducedDailyRestsUsed { get; }

    /// <summary>End of the rest.</summary>
    public DateTime? LastDailyRestDate { get; }
    public RestType LastDailyRestType { get; }

    /// <summary>End of the rest.</summary>
    public DateTime? LastWeeklyRestDate { get; }
    public RestType LastWeeklyRestType { get; }

    /// <summary>When this snapshot was taken.</summary>
    public DateTime CalculatedAt { get; }

    // Only for EF Core.
    public DrivingLimits()
    {

    }

    public DrivingLimits(
        TimeSpan drivingToday,
        TimeSpan drivingThisWeek,
        TimeSpan drivingLastTwoWeeks,
        TimeSpan drivingSinceLastBreak,
        int extendedDaysUsedThisWeek,
        int reducedDailyRestsUsed,
        DateTime? lastDailyRestDate,
        RestType lastDailyRestType,
        DateTime? lastWeeklyRestDate,
        RestType lastWeeklyRestType)
    {
        DrivingToday = drivingToday;
        DrivingThisWeek = drivingThisWeek;
        DrivingLastTwoWeeks = drivingLastTwoWeeks;
        DrivingSinceLastBreak = drivingSinceLastBreak;
        ExtendedDaysUsedThisWeek = extendedDaysUsedThisWeek;
        ReducedDailyRestsUsed = reducedDailyRestsUsed;
        LastDailyRestDate = lastDailyRestDate;
        LastDailyRestType = lastDailyRestType;
        LastWeeklyRestDate = lastWeeklyRestDate;
        LastWeeklyRestType = lastWeeklyRestType;
        CalculatedAt = DateTime.UtcNow;
    }

    public TimeSpan AvailableToday()
    {
        var maxToday = CanUseExtendedDay() ? MaxDailyDrivingExtended : MaxDailyDriving;
        var remaining = maxToday - DrivingToday;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public TimeSpan AvailableThisWeek()
    {
        var remaining = MaxWeeklyDriving - DrivingThisWeek;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public TimeSpan AvailableInTwoWeeks()
    {
        var remaining = MaxBiWeeklyDriving - DrivingLastTwoWeeks;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Art. 6.1: the 10h day, if used less than twice this week.</summary>
    public bool CanUseExtendedDay()
    {
        return ExtendedDaysUsedThisWeek < MaxExtendedDaysPerWeek;
    }

    /// <summary>Art. 8.4: the 9h rest, if used less than 3 times since the last weekly rest.</summary>
    public bool CanUseReducedDailyRest()
    {
        return ReducedDailyRestsUsed < MaxReducedDailyRests;
    }

    /// <summary>Art. 7: before the mandatory 45-minute break.</summary>
    public TimeSpan RemainingContinuousDrivingTime
    {
        get
        {
            var remaining = MaxContinuousDriving - DrivingSinceLastBreak;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Art. 7: the trip, added to the driving since the last break, must not exceed 4.5h.</summary>
    public bool CanDriveContinuous(TimeSpan tripDuration) =>
        tripDuration <= TimeSpan.Zero || (DrivingSinceLastBreak + tripDuration) <= MaxContinuousDriving;

    public bool CanDrive(TimeSpan tripDuration)
    {
        return tripDuration <= AvailableToday()
            && tripDuration <= AvailableThisWeek()
            && tripDuration <= AvailableInTwoWeeks()
            && CanDriveContinuous(tripDuration);
    }

    /// <summary>Art. 8.6: a weekly rest is required within 6 consecutive 24h periods.</summary>
    /// <param name="asOf">Typically the hypothetical trip start; a parameter, not the clock, to keep the engine deterministic.</param>
    public bool NeedsWeeklyRest(DateTime asOf)
    {
        if (LastWeeklyRestDate == null)
        {
            return true;
        }

        return asOf - LastWeeklyRestDate.Value >= MaxIntervalBetweenWeeklyRests;
    }

    /// <summary>Art. 8.2: rest and work share one cycle of 24h, so 11h of rest leave 13h of availability.</summary>
    public bool NeedsDailyRest()
    {
        if (LastDailyRestDate == null)
        {
            return true;
        }

        return DateTime.UtcNow - LastDailyRestDate.Value >= DailyRestWindow - DailyRestNormal;
    }

    public static DrivingLimits Empty() => new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        0,
        0,
        null,
        RestType.None,
        null,
        RestType.None
    );

    public override string ToString() =>
        $"Today: {DrivingToday.TotalHours:F1}h / Week: {DrivingThisWeek.TotalHours:F1}h / BiWeekly: {DrivingLastTwoWeeks.TotalHours:F1}h";
}