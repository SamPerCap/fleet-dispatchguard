namespace DispatchGuard.Core.ValueObjects;

/// <summary>A service already held by a driver or vehicle.</summary>
public sealed record HeldService(int TripId, TimeWindow Window, GeoCoordinate Origin, GeoCoordinate Destination);

/// <summary>Where the distance to a departure is measured from.</summary>
public enum ReachSource
{
    Unknown = 0,
    PreviousService = 1,
    HomeLocation = 2
}

/// <summary>
/// Position of a driver or vehicle for a service and whether it gets there in time (SPEC 0062 R4, R6). The position is the
/// destination of the previous service of the same working day (a shorter gap than <see cref="DrivingLimits.DailyRestReduced"/>,
/// the engine's own cut), otherwise its reference location. Arriving is an estimate: straight line at
/// <see cref="TravelAssumptions.AverageSpeedKmh"/> from the previous service and to the next one, whatever their day.
/// </summary>
public sealed record ServiceReach(double? DistanceKm, ReachSource Source, int? FromTripId, int? LateFromTripId, int? LateToTripId)
{
    public bool Arrives => LateFromTripId is null && LateToTripId is null;

    public double? StepKm => RecommendationSteps.StepOf(DistanceKm);

    /// <summary>For the dispatcher; null when it arrives.</summary>
    public LocalizedText? LateReason =>
        LateFromTripId is int from ? Texts.LateFromService(from, TravelAssumptions.AverageSpeedKmh)
        : LateToTripId is int to ? Texts.LateToService(to, TravelAssumptions.AverageSpeedKmh)
        : null;

    public static ServiceReach For(
        TimeWindow window, GeoCoordinate origin, GeoCoordinate destination, GeoCoordinate? home, IEnumerable<HeldService> held)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(held);

        HeldService? previous = null, next = null;
        foreach (var service in held)
        {
            if (service.Window.End <= window.Start && (previous is null || service.Window.End > previous.Window.End))
            {
                previous = service;
            }

            if (service.Window.Start >= window.End && (next is null || service.Window.Start < next.Window.Start))
            {
                next = service;
            }
        }

        var sameDay = previous is not null && window.Start - previous.Window.End < DrivingLimits.DailyRestReduced;
        var distance = sameDay ? previous!.Destination.DistanceTo(origin) : home?.DistanceTo(origin);
        var source = sameDay ? ReachSource.PreviousService : home is not null ? ReachSource.HomeLocation : ReachSource.Unknown;

        int? lateFrom = previous is not null && !Fits(previous.Destination, origin, window.Start - previous.Window.End) ? previous.TripId : null;
        int? lateTo = next is not null && !Fits(destination, next.Origin, next.Window.Start - window.End) ? next.TripId : null;
        return new ServiceReach(distance, source, sameDay ? previous!.TripId : null, lateFrom, lateTo);
    }

    private static bool Fits(GeoCoordinate from, GeoCoordinate to, TimeSpan gap) => TravelAssumptions.TravelTime(from.DistanceTo(to)) <= gap;
}
