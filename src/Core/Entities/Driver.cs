using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class Driver : EntityWithUnavailabilities<DriverUnavailability>
{
    public const int MaxIdentificationNumberLength = 50;
    public const int MaxNameLength = 100;

    public string IdentificationNumber { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}";

    public bool IsExternal { get; private set; }
    public bool AllowsAutoAssignment { get; private set; }
    public DateTime? HireDate { get; private set; }
    public DateTime? TerminationDate { get; private set; }

    public DrivingLimits? CurrentLimits { get; private set; }

    /// <summary>
    /// As declared from the tachograph (SPEC 0036). Only stored: the CE 561/2006 evaluation does not use it.
    /// </summary>
    public DateTime? DeclaredLastWeeklyRestEnd { get; private set; }

    private Driver() { }

    public int BaseId { get; private set; } = OperatingBase.DefaultId;

    public void MoveToBase(int baseId)
    {
        EntityValidator.ValidatePositive(baseId, nameof(baseId));
        BaseId = baseId;
        MarkAsUpdated();
    }

    public Driver(
        string identificationNumber,
        string firstName,
        string lastName,
        bool isExternal = false,
        bool allowsAutoAssignment = true)
    {
        SetDetails(identificationNumber, firstName, lastName, isExternal, allowsAutoAssignment);
    }

    /// <summary>All or nothing. The last two only decide whether the driver is proposed, never the legal validation.</summary>
    public void ChangeDetails(string identificationNumber, string firstName, string lastName, bool isExternal, bool allowsAutoAssignment)
    {
        SetDetails(identificationNumber, firstName, lastName, isExternal, allowsAutoAssignment);
        MarkAsUpdated();
    }

    private void SetDetails(string identificationNumber, string firstName, string lastName, bool isExternal, bool allowsAutoAssignment)
    {
        var validNumber = ResourceDetailRules.Text(identificationNumber, MaxIdentificationNumberLength, Texts.IdentificationMissing(), Texts.IdentificationCannot());
        var validFirstName = ResourceDetailRules.Text(firstName, MaxNameLength, Texts.NameMissing(), Texts.NameCannot());
        var validLastName = ResourceDetailRules.Text(lastName, MaxNameLength, Texts.LastNameMissing(), Texts.LastNameCannot());

        IdentificationNumber = validNumber.ToUpperInvariant();
        FirstName = validFirstName;
        LastName = validLastName;
        IsExternal = isExternal;
        AllowsAutoAssignment = allowsAutoAssignment;
    }

    public void Hire(DateTime hireDate)
    {
        ValidateHireDate(hireDate);
        HireDate = hireDate;
        Reactivate();
    }

    public void Terminate(DateTime terminationDate)
    {
        ValidateTerminationDate(terminationDate);
        TerminationDate = terminationDate;
        Deactivate();
    }

    public void EnableAutoAssignment()
    {
        AllowsAutoAssignment = true;
        MarkAsUpdated();
    }

    public void DisableAutoAssignment()
    {
        AllowsAutoAssignment = false;
        MarkAsUpdated();
    }

    public void UpdateDrivingLimits(DrivingLimits limits)
    {
        ValidateDrivingLimits(limits);
        CurrentLimits = limits;
        MarkAsUpdated();
    }

    /// <summary><paramref name="restEnd"/> cannot be after <paramref name="now"/>.</summary>
    public void DeclareLastWeeklyRest(DateTime restEnd, DateTime now)
    {
        if (restEnd > now)
        {
            throw new InvalidRequestException(Texts.FieldInFuture(nameof(restEnd)));
        }

        DeclaredLastWeeklyRestEnd = restEnd;
        MarkAsUpdated();
    }

    public bool CanPerformTrip(TimeWindow tripWindow, TimeSpan estimatedDrivingTime)
    {
        if (!IsActive)
        {
            return false;
        }

        if (!IsAvailableDuring(tripWindow))
        {
            return false;
        }

        if (IsExternal)
        {
            return true;
        }

        if (CurrentLimits == null)
        {
            throw new InvalidOperationException("Driving limits not calculated for driver");
        }

        return CurrentLimits.CanDrive(estimatedDrivingTime);
    }

    /// <summary>
    /// A reduced rest is only granted if <see cref="CurrentLimits"/> still allows one (unknown limits do); otherwise it falls
    /// back to the normal rest (SPEC 0030).
    /// </summary>
    public DriverAvailability GetAvailability(DateTime day, bool useReducedDailyRest, IEnumerable<TimeWindow> committedWork)
    {
        var reduced = useReducedDailyRest && (CurrentLimits?.CanUseReducedDailyRest() ?? true);
        return DriverAvailability.For(day, reduced, committedWork);
    }

    /// <summary>
    /// No unavailability overlaps the trip and it fits the availability of the day it starts on, given the work already
    /// committed. Assumes the normal daily rest, to be conservative (SPEC 0030).
    /// </summary>
    public bool HasAvailabilityFor(TimeWindow tripWindow, IEnumerable<TimeWindow> committedWork)
    {
        ArgumentNullException.ThrowIfNull(tripWindow);
        return IsAvailableDuring(tripWindow)
            && GetAvailability(tripWindow.Start, useReducedDailyRest: false, committedWork).Fits(tripWindow);
    }

    public bool IsEligibleForAutoAssignment() => IsActive && AllowsAutoAssignment && !IsExternal;

    private static void ValidateHireDate(DateTime hireDate)
    {
        EntityValidator.ValidateNotFuture(hireDate, nameof(hireDate));
    }

    private static void ValidateTerminationDate(DateTime terminationDate)
    {
        EntityValidator.ValidateNotFuture(terminationDate, nameof(terminationDate));
    }

    private static void ValidateDrivingLimits(DrivingLimits? limits)
    {
        EntityValidator.ValidateNotNull(limits, nameof(limits));
    }
}