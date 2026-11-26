namespace DispatchGuard.Core.Enums;

public enum AllocationOutcome
{
    Assigned = 0,
    RejectedCompliance = 1,
    RejectedVehicle = 2,
    RejectedSchedule = 3,
    /// <summary>Assigned past a CE 561/2006 rejection (SPEC 0056): it does not count as a rejection.</summary>
    Forced = 4,
    /// <summary>Inactive or unavailable during the service. Never forcible (SPEC 0066).</summary>
    RejectedDriver = 5
}
