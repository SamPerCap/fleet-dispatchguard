namespace DispatchGuard.Core.Enums;

/// <summary>Rest periods of CE 561/2006; the figures are in <see cref="ValueObjects.DrivingLimits"/>.</summary>
public enum RestType
{
    None = 0,

    /// <summary>11 consecutive hours.</summary>
    DailyNormal = 1,

    /// <summary>9 consecutive hours, at most 3 times between weekly rests.</summary>
    DailyReduced = 2,

    /// <summary>45 consecutive hours.</summary>
    WeeklyNormal = 3,

    /// <summary>24 consecutive hours; must be compensated before the end of the third week.</summary>
    WeeklyReduced = 4
}