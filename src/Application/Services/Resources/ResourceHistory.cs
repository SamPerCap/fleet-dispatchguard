using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Resources;

/// <summary><see cref="Km"/> is a straight-line estimate, null when not estimable (SPEC 0061 R6).</summary>
public sealed record HistoryTrip(int TripId, DateTime Start, DateTime End, TripStatus Status, double? Km);

/// <summary>One Madrid day: the time driven or used inside it, and the services and km that started on it.</summary>
public sealed record HistoryDay(DateTime From, DateTime To, TimeSpan Duration, int Trips, double Km);

/// <summary>
/// <see cref="DaysOverDaily"/>/<see cref="DaysOverExtended"/>: Madrid days strictly above
/// <see cref="ResourceHistory.DailyLimit"/>/<see cref="ResourceHistory.ExtendedDailyLimit"/>.
/// </summary>
public sealed record HistoryTotals(TimeSpan Duration, int Trips, double Km, int TripsWithoutKm, int DaysWorked, int DaysOverDaily, int DaysOverExtended);

/// <summary>
/// Only finished services count (SPEC 0061 R1), over Madrid days (R2); time is split by day (R3), services and km go to the day
/// they start (R5). The bars inform: the engine still decides compliance with its own working-day calculation.
/// </summary>
public static class ResourceHistory
{
    /// <summary>Today and the previous 27.</summary>
    public const int Days = 28;

    /// <summary>The two marks of the driver's chart, straight from <see cref="DrivingLimits"/>.</summary>
    public static readonly TimeSpan DailyLimit = DrivingLimits.MaxDailyDriving;
    public static readonly TimeSpan ExtendedDailyLimit = DrivingLimits.MaxDailyDrivingExtended;

    /// <summary>The same as the driving history (<c>DrivingRecordRepository</c>).</summary>
    private static readonly IReadOnlySet<TripStatus> DrivingStatuses =
        new HashSet<TripStatus> { TripStatus.Assigned, TripStatus.ManuallyAssigned, TripStatus.InProgress, TripStatus.Completed };

    /// <summary>Madrid days ending with the one that contains <paramref name="asOf"/>, oldest first.</summary>
    public static IReadOnlyList<(DateTime From, DateTime To)> Window(DateTime asOf)
    {
        var days = new List<(DateTime From, DateTime To)> { OperationalDay.Containing(asOf) };
        while (days.Count < Days) days.Insert(0, OperationalDay.Before(days[0].From));
        return days;
    }

    /// <summary>Assigned, not cancelled and already finished at <paramref name="asOf"/>.</summary>
    public static bool IsDone(TripStatus status, DateTime end, DateTime asOf) => DrivingStatuses.Contains(status) && end <= asOf;

    /// <summary>Done and with some time inside the window.</summary>
    public static bool InWindow(HistoryTrip trip, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return IsDone(trip.Status, trip.End, asOf) && trip.End > Window(asOf)[0].From;
    }

    /// <summary>Every day of the window, oldest first. Trips that are not done are ignored.</summary>
    public static IReadOnlyList<HistoryDay> Daily(IEnumerable<HistoryTrip> trips, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(trips);
        var window = Window(asOf);
        var done = trips.Where(t => InWindow(t, asOf)).ToList();
        return window.Select((d, i) =>
        {
            var duration = TimeSpan.FromTicks(done.Sum(t => Overlap(t.Start, t.End, d.From, d.To).Ticks));
            // By start day; a service that started before the window goes to its first day.
            var starting = done.Where(t => (i == 0 ? t.Start < d.To : t.Start >= d.From && t.Start < d.To)).ToList();
            return new HistoryDay(d.From, d.To, duration, starting.Count, Math.Round(starting.Sum(t => t.Km ?? 0), 1));
        }).ToList();
    }

    /// <param name="days">From <see cref="Daily"/>.</param>
    /// <param name="doneInWindow">Trips that passed <see cref="InWindow"/>.</param>
    public static HistoryTotals Totals(IReadOnlyList<HistoryDay> days, IReadOnlyCollection<HistoryTrip> doneInWindow)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(doneInWindow);
        return new HistoryTotals(
            TimeSpan.FromTicks(days.Sum(d => d.Duration.Ticks)),
            days.Sum(d => d.Trips),
            Math.Round(days.Sum(d => d.Km), 1),
            doneInWindow.Count(t => t.Km is null),
            days.Count(d => d.Duration > TimeSpan.Zero),
            days.Count(d => d.Duration > DailyLimit),
            days.Count(d => d.Duration > ExtendedDailyLimit));
    }

    /// <summary>
    /// Straight line along origin → stops → destination, rounded to 0.1 km. Null when the route has no length (same origin and
    /// destination without stops): it is never invented (SPEC 0061 R6).
    /// </summary>
    public static double? EstimateKm(IReadOnlyList<GeoCoordinate> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var km = route.Zip(route.Skip(1), (a, b) => a.DistanceTo(b)).Sum();
        return km > 0 ? Math.Round(km, 1) : null;
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var sliceStart = start > from ? start : from;
        var sliceEnd = end < to ? end : to;
        return sliceEnd > sliceStart ? sliceEnd - sliceStart : TimeSpan.Zero;
    }
}
