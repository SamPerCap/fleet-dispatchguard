namespace DispatchGuard.Core.Enums;

/// <summary>Shared by the database queries and the simulation, so that both read statuses the same way (SPEC 0062).</summary>
public static class TripStatusRules
{
    /// <summary>Trips in these statuses count as driving of their driver.</summary>
    public static readonly TripStatus[] Driving =
    [
        TripStatus.Assigned,
        TripStatus.ManuallyAssigned,
        TripStatus.InProgress,
        TripStatus.Completed
    ];
}
