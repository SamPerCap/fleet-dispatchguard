namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Real driving hours at an instant, measured with the same calculation as <see cref="LegalComplianceEvaluation"/>; limits come
/// from <see cref="DrivingLimits"/>. <c>Today</c> is the working day between daily rests; weeks are natural, the strictest of
/// Madrid time and UTC; <c>LastTwoWeeks</c> is the previous week plus this one (SPEC 0064).
/// </summary>
public sealed record DriverHours(
    TimeSpan Today,
    TimeSpan ThisWeek,
    TimeSpan LastTwoWeeks,
    TimeSpan SinceLastWeeklyRest,
    DateTime? LastWeeklyRestEnd)
{
    public static TimeSpan MaxDaily => DrivingLimits.MaxDailyDriving;
    public static TimeSpan MaxWeekly => DrivingLimits.MaxWeeklyDriving;
    public static TimeSpan MaxBiWeekly => DrivingLimits.MaxBiWeeklyDriving;

    public static DriverHours Of(TimeSpan today, TimeSpan thisWeek, TimeSpan lastTwoWeeks, TimeSpan sinceLastWeeklyRest, DateTime? lastWeeklyRestEnd) =>
        new(today, thisWeek, lastTwoWeeks, sinceLastWeeklyRest, lastWeeklyRestEnd);
}
