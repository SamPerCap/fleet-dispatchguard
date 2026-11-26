namespace DispatchGuard.Core.Enums;

/// <summary>Return-to-base rule of Amendment 2020/1054, within a 4-week window.</summary>
public enum ReturnToBaseStatus
{
    /// <summary>Returned to, or departed from, the home location within the window.</summary>
    Compliant = 0,

    /// <summary>Has a home location but did not return to it within the window.</summary>
    NonCompliant = 1,

    /// <summary>Unknown driver or no home location assigned.</summary>
    NotVerifiable = 2,
}
