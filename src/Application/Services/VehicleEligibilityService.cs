using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

public interface IVehicleEligibilityService
{
    /// <summary>Accumulates ALL the violations rather than stopping at the first one.</summary>
    VehicleEligibilityCheck CheckEligibility(Vehicle vehicle, TimeWindow tripWindow, int requiredSeats);
}

public sealed class VehicleEligibilityService : IVehicleEligibilityService
{
    public VehicleEligibilityCheck CheckEligibility(Vehicle vehicle, TimeWindow tripWindow, int requiredSeats)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(tripWindow);

        var violations = new List<ComplianceViolation>();

        if (!vehicle.IsActive)
        {
            violations.Add(ComplianceViolation.Critical("VehicleEligibility.NotActive", vehicle.DecommissionedDate is { } retired
                ? Texts.VehicleRetired(retired.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture))
                : Texts.VehicleNotActive()));
        }

        if (!vehicle.HasCapacityFor(requiredSeats))
        {
            violations.Add(ComplianceViolation.Critical(
                "VehicleEligibility.InsufficientCapacity",
                Texts.NotEnoughSeats(vehicle.SeatingCapacity, requiredSeats)));
        }

        // GetUnavailabilitiesInRange uses strict comparisons, so adjacent but not overlapping periods are already excluded.
        foreach (var unavailability in vehicle.GetUnavailabilitiesInRange(tripWindow.Start, tripWindow.End).OrderBy(u => u.Period.Start))
        {
            violations.Add(ComplianceViolation.Critical(
                $"VehicleEligibility.Unavailable.{unavailability.Type}",
                Texts.VehicleNotAvailable(DescribeType(unavailability.Type), unavailability.Period.Start, unavailability.Period.End)));
        }

        return violations.Count == 0
            ? VehicleEligibilityCheck.Eligible()
            : VehicleEligibilityCheck.Ineligible(violations);
    }

    private static LocalizedText DescribeType(VehicleUnavailabilityType type) => type switch
    {
        VehicleUnavailabilityType.Maintenance => Texts.Maintenance(),
        VehicleUnavailabilityType.TechnicalInspection => Texts.TechnicalInspection(),
        VehicleUnavailabilityType.Repair => Texts.Repair(),
        VehicleUnavailabilityType.Cleaning => Texts.Cleaning(),
        VehicleUnavailabilityType.Administrative => Texts.Administrative(),
        _ => Texts.OtherUnavailability()
    };
}
