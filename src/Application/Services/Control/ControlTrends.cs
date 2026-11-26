using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Application.Services.Control;

/// <summary><c>Date</c> is the UTC instant in which the operational day starts.</summary>
public sealed record DailyTripsDto(DateTime Date, int TotalTrips, int CoveredTrips);

/// <summary>Oldest day first; a null <c>BaseId</c> means every base.</summary>
public sealed record ControlTrendDto(int? BaseId, IReadOnlyList<DailyTripsDto> Days);

/// <summary><c>Validations = Accepted + Blocked + Forced</c>.</summary>
public sealed record EngineHourDto(DateTime Hour, int Validations, int Accepted, int Blocked, int Forced);

/// <summary>Over the operational day [<c>From</c>, <c>To</c>).</summary>
public sealed record EngineHourlyDto(int? BaseId, DateTime From, DateTime To, IReadOnlyList<EngineHourDto> Hours);

/// <summary>
/// They reuse the definitions of <c>summary</c> and <c>engine-metrics</c> (SPEC 0057), so that the last day of the trend equals
/// the summary and the hours add up to the day.
/// </summary>
public static class ControlTrends
{
    public const int DefaultDays = 7;
    public const int MinDays = 2;
    public const int MaxDays = 31;

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    /// <summary>Ending with the operational day that contains <paramref name="date"/>, oldest first.</summary>
    public static IReadOnlyList<(DateTime From, DateTime To)> Days(DateTime date, int days)
    {
        if (days is < MinDays or > MaxDays) throw new InvalidRequestException(Texts.FieldBetween(nameof(days), MinDays, MaxDays));
        var list = new List<(DateTime From, DateTime To)> { OperationalDay.Containing(date) };
        while (list.Count < days) list.Insert(0, OperationalDay.Before(list[0].From));
        return list;
    }

    /// <summary>
    /// Per day, the active non-cancelled trips that overlap it: the same rule as <c>summary</c>, so a trip that crosses midnight
    /// counts on both days.
    /// </summary>
    public static IReadOnlyList<DailyTripsDto> Daily(IEnumerable<Trip> trips, IReadOnlyList<(DateTime From, DateTime To)> days)
    {
        var live = trips.Where(t => t.IsActive && t.Status != TripStatus.Cancelled).ToList();
        return days.Select(d =>
        {
            var on = live.Where(t => t.ScheduledWindow.Start < d.To && t.ScheduledWindow.End > d.From).ToList();
            return new DailyTripsDto(d.From, on.Count, on.Count(ControlRules.IsCovered));
        }).ToList();
    }

    /// <summary>
    /// Madrid's offsets are whole hours, so one-hour UTC slots match local hours: 23 on the spring DST day and 25 on the autumn one.
    /// </summary>
    public static IReadOnlyList<(DateTime From, DateTime To)> Hours(DateTime from, DateTime to)
    {
        var list = new List<(DateTime From, DateTime To)>();
        for (var h = from; h < to; h += Hour) list.Add((h, h + Hour < to ? h + Hour : to));
        return list;
    }

    /// <summary>Rejected outcomes count as blocked, as in <c>engine-metrics</c>.</summary>
    public static IReadOnlyList<EngineHourDto> Hourly(IEnumerable<AllocationAttempt> attempts, DateTime from, DateTime to)
    {
        var list = attempts.Where(a => a.AttemptedAt >= from && a.AttemptedAt < to).ToList();
        return Hours(from, to).Select(h =>
        {
            var inHour = list.Where(a => a.AttemptedAt >= h.From && a.AttemptedAt < h.To).ToList();
            var accepted = inHour.Count(a => a.Outcome == AllocationOutcome.Assigned);
            var forced = inHour.Count(a => a.Outcome == AllocationOutcome.Forced);
            return new EngineHourDto(h.From, inHour.Count, accepted, inHour.Count(a => a.IsRejected), forced);
        }).ToList();
    }
}
