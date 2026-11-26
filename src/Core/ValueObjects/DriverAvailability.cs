namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// 24h − daily rest (13h normal, 15h reduced), measured as the span from the start of the first work to the end of the last
/// work of the day, because the daily rest must be taken in one block within those 24h. A coarse, conservative filter before
/// the CE 561/2006 evaluation, never an alternative to it. Driving, other work and presence time are not distinguished: there
/// is no data for them yet (SPEC 0030).
/// </summary>
public sealed class DriverAvailability
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private readonly IReadOnlyList<TimeWindow> _committedWork;

    /// <summary>Calendar day [Day, Day + 24h).</summary>
    public DateTime Day { get; }

    public bool UsesReducedDailyRest { get; }

    /// <summary>13h (normal rest) or 15h (reduced). Hard cap of every value reported here.</summary>
    public TimeSpan DailyAvailableWindow { get; }

    /// <summary>Window minus the span already taken by committed work; never negative.</summary>
    public TimeSpan RemainingAvailability { get; }

    private DriverAvailability(DateTime day, bool useReducedDailyRest, IReadOnlyList<TimeWindow> committedWork)
    {
        Day = day.Date;
        UsesReducedDailyRest = useReducedDailyRest;
        DailyAvailableWindow = OneDay - (useReducedDailyRest ? DrivingLimits.DailyRestReduced : DrivingLimits.DailyRestNormal);
        _committedWork = committedWork;

        var used = Span(_committedWork);
        RemainingAvailability = used >= DailyAvailableWindow ? TimeSpan.Zero : DailyAvailableWindow - used;
    }

    /// <summary>Only committed work overlapping <paramref name="day"/> counts.</summary>
    public static DriverAvailability For(DateTime day, bool useReducedDailyRest, IEnumerable<TimeWindow> committedWork)
    {
        ArgumentNullException.ThrowIfNull(committedWork);

        var dayWindow = new TimeWindow(day.Date, day.Date + OneDay);
        var inDay = committedWork.Where(w => w.OverlapsWith(dayWindow)).ToList();
        return new DriverAvailability(day, useReducedDailyRest, inDay);
    }

    /// <summary>True if the day's work span, with <paramref name="tripWindow"/>, stays within the window.</summary>
    public bool Fits(TimeWindow tripWindow)
    {
        ArgumentNullException.ThrowIfNull(tripWindow);
        return Span([.. _committedWork, tripWindow]) <= DailyAvailableWindow;
    }

    private static TimeSpan Span(IReadOnlyCollection<TimeWindow> work) =>
        work.Count == 0 ? TimeSpan.Zero : work.Max(w => w.End) - work.Min(w => w.Start);
}
