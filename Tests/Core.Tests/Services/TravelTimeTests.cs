using DispatchGuard.Application.Services.TravelTime;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Tests.Services;

public class TravelTimeTests
{
    private static Location At(decimal lat, decimal lon) =>
        new("L", "Main St 1", new GeoCoordinate(lat, lon), "Madrid", "Madrid");

    private static readonly Location PointA = At(40.0m, -3.0m);
    private static readonly Location PointB = At(40.0m, -2.0m);

    [Fact]
    public void Estimate_NoSamples_UsesStraightLineAtAverageSpeed()
    {
        var km = PointA.Coordinate.DistanceTo(PointB.Coordinate);

        var result = TripDurationEstimator.Estimate([], PointA, PointB, sameLocation: false);

        Assert.Equal(TravelTimeSource.Estimated, result.Source);
        Assert.Equal(0, result.Samples);
        Assert.Equal((int)Math.Round(TravelAssumptions.TravelTime(km).TotalMinutes), result.Minutes);
    }

    [Fact]
    public void Estimate_SeveralSamples_ReturnsRoundedAverage()
    {
        var result = TripDurationEstimator.Estimate(
            [TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(70), TimeSpan.FromMinutes(61)], PointA, PointB, sameLocation: false);

        Assert.Equal(TravelTimeSource.Historical, result.Source);
        Assert.Equal(3, result.Samples);
        Assert.Equal(60, result.Minutes); // 60.33 → 60
    }

    [Fact]
    public void Estimate_SameLocationWithoutSamples_IsNone()
    {
        var result = TripDurationEstimator.Estimate([], PointA, PointA, sameLocation: true);

        Assert.Equal(TravelTimeSource.None, result.Source);
        Assert.Null(result.Minutes);
    }

    [Fact]
    public void Estimate_SameLocationWithSamples_UsesHistory()
    {
        var result = TripDurationEstimator.Estimate([TimeSpan.FromHours(3)], PointA, PointA, sameLocation: true);

        Assert.Equal(TravelTimeSource.Historical, result.Source);
        Assert.Equal(180, result.Minutes);
    }

    [Fact]
    public void Cache_Invalidate_DropsEntries()
    {
        var cache = new TripDurationCache();
        var value = new TravelTimeEstimate(60, TravelTimeSource.Historical, 2);
        cache.Set((1, 2), value, cache.Generation);

        Assert.True(cache.TryGet((1, 2), out var hit));
        Assert.Equal(value, hit);

        cache.Invalidate();

        Assert.False(cache.TryGet((1, 2), out _));
    }

    [Fact]
    public void Cache_SetComputedBeforeInvalidate_IsDiscarded()
    {
        var cache = new TripDurationCache();
        var generation = cache.Generation; // A read starts…
        cache.Invalidate(); // …a trip is saved meanwhile…
        cache.Set((1, 2), new TravelTimeEstimate(60, TravelTimeSource.Historical, 2), generation);

        Assert.False(cache.TryGet((1, 2), out _)); // …so the stale value is not cached.
    }
}
