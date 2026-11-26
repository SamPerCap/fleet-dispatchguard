using System.Collections.Concurrent;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.TravelTime;

public enum TravelTimeSource
{
    /// <summary>No sensible figure: same location and no recorded trips.</summary>
    None,
    /// <summary>Average of recorded trips between the two locations.</summary>
    Historical,
    /// <summary>Straight line at <see cref="TravelAssumptions.AverageSpeedKmh"/>.</summary>
    Estimated
}

/// <summary>Whole minutes; null when <see cref="TravelTimeSource.None"/>.</summary>
public sealed record TravelTimeEstimate(int? Minutes, TravelTimeSource Source, int Samples);

public static class TripDurationEstimator
{
    public static TravelTimeEstimate Estimate(IReadOnlyCollection<TimeSpan> samples, Location origin, Location destination, bool sameLocation)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        if (samples.Count > 0)
            return new(Round(samples.Average(s => s.TotalMinutes)), TravelTimeSource.Historical, samples.Count);
        if (sameLocation)
            return new(null, TravelTimeSource.None, 0);
        var km = origin.Coordinate.DistanceTo(destination.Coordinate);
        return new(Round(TravelAssumptions.TravelTime(km).TotalMinutes), TravelTimeSource.Estimated, 0);
    }

    private static int Round(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
}

/// <summary>
/// A value computed before an <see cref="Invalidate"/> is discarded (generation check), so that a save racing a read never
/// leaves stale data (SPEC 0055 R5). Must be a singleton.
/// </summary>
public sealed class TripDurationCache : ITripDurationCache
{
    private readonly ConcurrentDictionary<(int, int), TravelTimeEstimate> _entries = new();
    private readonly object _gate = new();
    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    public bool TryGet((int Origin, int Destination) key, out TravelTimeEstimate value) =>
        _entries.TryGetValue(key, out value!);

    public void Set((int Origin, int Destination) key, TravelTimeEstimate value, long generation)
    {
        lock (_gate)
        {
            if (generation == _generation) _entries[key] = value;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            _entries.Clear();
        }
    }
}

public interface ITravelTimeService
{
    Task<TravelTimeEstimate> GetAsync(int originLocationId, int destinationLocationId, CancellationToken ct = default);
}

public sealed class TravelTimeService(IUnitOfWork uow, TripDurationCache cache) : ITravelTimeService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    private readonly TripDurationCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public async Task<TravelTimeEstimate> GetAsync(int originLocationId, int destinationLocationId, CancellationToken ct = default)
    {
        var origin = await ActiveLocationAsync(originLocationId, ct);
        var destination = await ActiveLocationAsync(destinationLocationId, ct);
        var key = (originLocationId, destinationLocationId);
        if (_cache.TryGet(key, out var cached)) return cached;

        var generation = _cache.Generation;
        var samples = await _uow.Trips.GetDurationsAsync(originLocationId, destinationLocationId, ct);
        var result = TripDurationEstimator.Estimate(samples, origin, destination, originLocationId == destinationLocationId);
        _cache.Set(key, result, generation);
        return result;
    }

    private async Task<Location> ActiveLocationAsync(int id, CancellationToken ct)
    {
        var location = await _uow.Locations.GetByIdAsync(id, ct);
        return location is { IsActive: true } ? location : throw MissingResource.Location(id);
    }
}
