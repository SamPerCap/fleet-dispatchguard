using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Control;

public enum TrafficLight { Green, Amber, Red }

public enum DrivingLimitKind { Daily, Weekly, BiWeekly }

/// <summary>Limits always come from <see cref="DrivingLimits"/>.</summary>
public static class ControlRules
{
    /// <summary>An uncovered trip departing in less than this is urgent; in exactly 1 h, it is amber.</summary>
    public static readonly TimeSpan UrgentThreshold = TimeSpan.FromHours(1);

    /// <summary>At this share of any driving limit, or more, there is a warning (decision 0049).</summary>
    public const int NearLimitPercent = 90;

    public static bool IsUrgent(DateTime departure, DateTime now) => departure - now < UrgentThreshold;

    /// <summary>By incidents and not only by uncovered departures, so that an urgent absence is red too (SPEC 0057 R4).</summary>
    public static TrafficLight Light(int urgent, int warnings) =>
        urgent > 0 ? TrafficLight.Red : warnings > 0 ? TrafficLight.Amber : TrafficLight.Green;

    public static bool IsCovered(Trip trip) => trip.AssignedDriverId is not null && trip.AssignedVehicleId is not null;

    /// <summary>By schedule, start ≤ now &lt; end: there is no telemetry.</summary>
    public static bool InProgressBySchedule(Trip trip, DateTime now) =>
        trip.Status != TripStatus.Completed && IsCovered(trip)
        && trip.ScheduledWindow.Start <= now && now < trip.ScheduledWindow.End;

    /// <summary>By status, or covered and already past its scheduled end.</summary>
    public static bool CompletedBySchedule(Trip trip, DateTime now) =>
        trip.Status == TripStatus.Completed || (IsCovered(trip) && trip.ScheduledWindow.End <= now);

    /// <summary>Built only from counts.</summary>
    public static (LocalizedText Headline, LocalizedText Detail) Wording(int urgent, int warnings, int covered, int total)
    {
        var coverage = Texts.ServicesCovered(covered, total);
        // With nothing pending but uncovered trips that already ended, saying that everything is covered would be false.
        if (urgent + warnings == 0)
            return (covered == total ? Texts.EverythingCovered() : Texts.NothingPending(), coverage);

        return (Texts.TasksLeft(urgent + warnings), Texts.UrgentAndWarnings(urgent, warnings, coverage));
    }

    public static IReadOnlyList<DrivingLimitKind> LimitsNear(DriverHours hours)
    {
        var near = new List<DrivingLimitKind>();
        if (IsNear(hours.Today, DrivingLimits.MaxDailyDriving)) near.Add(DrivingLimitKind.Daily);
        if (IsNear(hours.ThisWeek, DrivingLimits.MaxWeeklyDriving)) near.Add(DrivingLimitKind.Weekly);
        if (IsNear(hours.LastTwoWeeks, DrivingLimits.MaxBiWeeklyDriving)) near.Add(DrivingLimitKind.BiWeekly);
        return near;
    }

    // Integer comparison: 8h06 of 9h is exactly 90 %, without floating point rounding.
    private static bool IsNear(TimeSpan driven, TimeSpan limit) => driven.Ticks * 100 >= limit.Ticks * NearLimitPercent;
}
