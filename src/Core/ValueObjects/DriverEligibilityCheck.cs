using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Active and with no unavailability, of any type, overlapping the service. The only place that decides it: allocating, the
/// candidate search and the Control Center read this check (SPEC 0066). Says nothing about CE 561/2006, overlaps with other
/// services or who may be suggested automatically.
/// </summary>
public sealed class DriverEligibilityCheck
{
    public const string NotActiveRule = "DriverEligibility.NotActive";
    public const string UnavailableRulePrefix = "DriverEligibility.Unavailable.";

    public bool IsEligible => Violations.Count == 0;

    public IReadOnlyList<ComplianceViolation> Violations { get; }

    private DriverEligibilityCheck(List<ComplianceViolation> violations) => Violations = violations.AsReadOnly();

    /// <summary>
    /// Against [departure, arrival) of the service, as UTC instants: an absence that ends exactly at the departure or starts
    /// exactly at the arrival does not count. Every reason is listed, absences by start.
    /// </summary>
    public static DriverEligibilityCheck For(Driver driver, TimeWindow service)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(service);

        var violations = new List<ComplianceViolation>();
        if (!driver.IsActive)
        {
            violations.Add(ComplianceViolation.Critical(NotActiveRule, Texts.DriverNotActive()));
        }

        foreach (var absence in driver.GetUnavailabilitiesInRange(service.Start, service.End).OrderBy(u => u.Period.Start))
        {
            violations.Add(ComplianceViolation.Critical(
                $"{UnavailableRulePrefix}{absence.Type}",
                Texts.DriverNotAvailable(DescribeType(absence.Type), absence.Period.Start, absence.Period.End)));
        }

        return new DriverEligibilityCheck(violations);
    }

    private static LocalizedText DescribeType(UnavailabilityType type) => type switch
    {
        UnavailabilityType.Vacation => Texts.Vacation(),
        UnavailabilityType.SickLeave => Texts.SickLeave(),
        UnavailabilityType.WeeklyRest => Texts.WeeklyRestAbsence(),
        UnavailabilityType.Training => Texts.Training(),
        UnavailabilityType.PersonalLeave => Texts.PersonalLeave(),
        _ => Texts.OtherUnavailability()
    };
}
