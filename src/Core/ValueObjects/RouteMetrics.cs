namespace DispatchGuard.Core.ValueObjects;

public sealed class RouteMetrics
{
    public double DistanceKm { get; }
    public TimeSpan EstimatedDuration { get; }
    public DateTime CalculatedAt { get; }

    // Only for EF Core.
    public RouteMetrics()
    {
    }

    public RouteMetrics(double distanceKm, TimeSpan estimatedDuration)
    {
        if (distanceKm < 0)
        {
            throw new ArgumentException("Distance cannot be negative", nameof(distanceKm));
        }

        if (estimatedDuration < TimeSpan.Zero)
        {
            throw new ArgumentException("Duration cannot be negative", nameof(estimatedDuration));
        }

        DistanceKm = distanceKm;
        EstimatedDuration = estimatedDuration;
        CalculatedAt = DateTime.UtcNow;
    }

    public override string ToString() => $"{DistanceKm:F2} km, {EstimatedDuration.TotalHours:F2}h";
}