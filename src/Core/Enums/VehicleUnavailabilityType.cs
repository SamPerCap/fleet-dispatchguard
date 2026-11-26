namespace DispatchGuard.Core.Enums;

public enum VehicleUnavailabilityType
{
    Maintenance = 1,

    /// <summary>Mandatory periodic inspection, ITV in Spain.</summary>
    TechnicalInspection = 2,

    /// <summary>Unscheduled, unlike <see cref="Maintenance"/>.</summary>
    Repair = 3,

    Cleaning = 4,

    Administrative = 5,

    Other = 99
}