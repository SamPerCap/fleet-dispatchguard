namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Single source of the approximations for empty (deadhead) legs: straight line until road distances arrive (SPEC 0033).
/// Operational, not regulatory.
/// </summary>
public static class TravelAssumptions
{
    /// <summary>Of an empty bus between two points.</summary>
    public static readonly double AverageSpeedKmh = 70;

    public static TimeSpan TravelTime(double km) => TimeSpan.FromHours(km / AverageSpeedKmh);
}
