using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Application.Services.Control;

/// <summary>Distinct vehicles in service during one hour.</summary>
public sealed record HourlyCountDto(DateTime Hour, int Count);

/// <summary>
/// <c>WindowStart</c>/<c>WindowEnd</c>: the operating day of the scope when every vehicle shares it; null when bases differ or
/// there are no vehicles.
/// </summary>
public sealed record FleetUtilization(
    DateTime? WindowStart, DateTime? WindowEnd, int ActiveVehicles,
    double AvailableVehicleHours, double AssignedVehicleHours, double UtilizationPercent,
    IReadOnlyList<HourlyCountDto> VehiclesInServiceByHour);

/// <summary>
/// Fleet utilization from stored data only, never a made-up index (SPEC 0058 R3, user decision, 2026-11-19).
/// <list type="bullet">
/// <item>Available = for each active vehicle of the scope, the FIXED operating day of its base (configurable,
/// <see cref="OperatingHoursSettings"/>) minus its unavailabilities.</item>
/// <item>Assigned = the part of that operating day covered by the vehicle's covered trips, minus its unavailabilities
/// (never above the available hours; a vehicle's overlapping trips are not counted twice).</item>
/// <item>Vehicles in service by hour: over the whole day, inside or outside the operating day.</item>
/// </list>
/// </summary>
public static class ControlEfficiency
{
    public static FleetUtilization Utilization(IReadOnlyList<Trip> dayTrips, IReadOnlyList<Vehicle> scopeVehicles, DateTime dayFrom, DateTime dayTo,
        Func<Vehicle, (DateTime From, DateTime To)> operatingDay)
    {
        ArgumentNullException.ThrowIfNull(operatingDay);
        var vehicles = scopeVehicles.Where(v => v.IsActive).ToList();
        var covered = dayTrips
            .Where(t => t.IsActive && t.Status != TripStatus.Cancelled && ControlRules.IsCovered(t)
                && t.ScheduledWindow.Start < dayTo && t.ScheduledWindow.End > dayFrom)
            .ToList();
        var day = (From: dayFrom, To: dayTo);

        double available = 0, assigned = 0;
        var busy = new List<List<(DateTime From, DateTime To)>>();
        var windows = new HashSet<(DateTime From, DateTime To)>();
        foreach (var vehicle in vehicles)
        {
            var dayWindow = operatingDay(vehicle);
            var window = (From: Max(dayWindow.From, dayFrom), To: Min(dayWindow.To, dayTo));
            windows.Add(window);
            var trips = covered.Where(t => t.AssignedVehicleId == vehicle.Id).Select(t => (t.ScheduledWindow.Start, t.ScheduledWindow.End)).ToList();
            busy.Add(Merge(trips, day));
            if (window.To <= window.From) continue;
            var off = Merge(vehicle.Unavailabilities.Select(u => (u.Period.Start, u.Period.End)), window);
            var own = Merge(trips, window);
            available += (window.To - window.From).TotalHours - Length(off);
            assigned += Length(own) - Length(Intersect(own, off));
        }

        var byHour = ControlTrends.Hours(dayFrom, dayTo).Select(h => new HourlyCountDto(h.From,
            busy.Count(intervals => intervals.Any(i => i.From < h.To && i.To > h.From)))).ToList();
        var percent = available > 0 ? Math.Round(assigned / available * 100, 1, MidpointRounding.AwayFromZero) : 0;
        var shared = windows.Count == 1 ? windows.Single() : ((DateTime From, DateTime To)?)null;
        return new FleetUtilization(shared?.From, shared?.To, vehicles.Count,
            Math.Round(available, 2), Math.Round(assigned, 2), percent, byHour);
    }

    /// <summary>Clipped to <paramref name="window"/>, sorted and without overlaps.</summary>
    private static List<(DateTime From, DateTime To)> Merge(IEnumerable<(DateTime From, DateTime To)> intervals, (DateTime From, DateTime To) window)
    {
        var merged = new List<(DateTime From, DateTime To)>();
        foreach (var (from, to) in intervals
                     .Select(i => (From: Max(i.From, window.From), To: Min(i.To, window.To)))
                     .Where(i => i.To > i.From)
                     .OrderBy(i => i.From))
        {
            if (merged.Count > 0 && from <= merged[^1].To)
                merged[^1] = (merged[^1].From, Max(merged[^1].To, to));
            else
                merged.Add((from, to));
        }
        return merged;
    }

    /// <summary>Both lists must already be merged.</summary>
    private static List<(DateTime From, DateTime To)> Intersect(List<(DateTime From, DateTime To)> first, List<(DateTime From, DateTime To)> second)
    {
        var result = new List<(DateTime From, DateTime To)>();
        foreach (var left in first)
            foreach (var right in second)
            {
                var from = Max(left.From, right.From);
                var to = Min(left.To, right.To);
                if (to > from) result.Add((from, to));
            }
        return result;
    }

    private static double Length(IEnumerable<(DateTime From, DateTime To)> intervals) => intervals.Sum(i => (i.To - i.From).TotalHours);
    private static DateTime Max(DateTime first, DateTime second) => first > second ? first : second;
    private static DateTime Min(DateTime first, DateTime second) => first < second ? first : second;
}
