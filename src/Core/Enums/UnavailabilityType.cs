namespace DispatchGuard.Core.Enums;

public enum UnavailabilityType
{
    Vacation = 1,

    SickLeave = 2,

    /// <summary>Mandatory under CE 561/2006.</summary>
    WeeklyRest = 3,

    Training = 4,

    PersonalLeave = 5,

    Other = 99
}