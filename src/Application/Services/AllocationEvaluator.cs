using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>Every step computed until the first rejection, and which one rejected the pair.</summary>
public sealed record AllocationVerdict(AllocationTrace Trace, AllocationOutcome? Rejection)
{
    public bool IsAllowed => Rejection is null;
}

/// <summary>
/// The checks of an assignment, in their contract order: available driver (SPEC 0066) → CE 561/2006 (with the later services,
/// SPEC 0063) → vehicle → schedule conflicts. Reads and decides, never writes: <see cref="DriverAllocationService"/> assigns with
/// it and a recommendation simulates with it (SPEC 0062, ADR 0020), so both reach the same verdict.
/// </summary>
public sealed class AllocationEvaluator
{
    private readonly ILegalValidationService _legal;
    private readonly IVehicleEligibilityService _vehicles;
    private readonly IScheduleConflictService _schedule;

    public AllocationEvaluator(ILegalValidationService legalValidationService, IVehicleEligibilityService vehicleEligibilityService, IScheduleConflictService scheduleConflictService)
    {
        _legal = legalValidationService ?? throw new ArgumentNullException(nameof(legalValidationService));
        _vehicles = vehicleEligibilityService ?? throw new ArgumentNullException(nameof(vehicleEligibilityService));
        _schedule = scheduleConflictService ?? throw new ArgumentNullException(nameof(scheduleConflictService));
    }

    /// <param name="pastCompliance">Go on after a CE 561/2006 rejection (a forced assignment, SPEC 0056); it is the only step that can be skipped.</param>
    public async Task<AllocationVerdict> EvaluateAsync(Driver driver, Vehicle vehicle, Trip trip, bool pastCompliance = false, CancellationToken cancellationToken = default)
    {
        var operation = OperationTrace.Current;
        AllocationVerdict verdict;
        try
        {
            verdict = await DecideAsync(driver, vehicle, trip, pastCompliance, cancellationToken);
        }
        finally
        {
            operation?.End();
        }

        if (operation is not null)
        {
            EvaluatedRules.AddTo(operation, verdict.Trace);
            operation.Decided(EvaluatedRules.ChecksOf(verdict));
        }

        return verdict;
    }

    private async Task<AllocationVerdict> DecideAsync(Driver driver, Vehicle vehicle, Trip trip, bool pastCompliance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(trip);

        // Whether the driver can work at all comes before CE 561/2006, so that a rejection nobody can force is never offered as
        // forcible (SPEC 0066 R6).
        OperationTrace.Current?.Begin(TraceStages.DriverAvailable);
        var availability = DriverEligibilityCheck.For(driver, trip.ScheduledWindow);
        var trace = new AllocationTrace(availability, null, null, null, null, trip.RequiredSeats, vehicle.SeatingCapacity);
        if (!availability.IsEligible) return new AllocationVerdict(trace, AllocationOutcome.RejectedDriver);

        OperationTrace.Current?.Begin(TraceStages.Compliance);
        var compliance = await _legal.ValidateDriverForTripDetailed(driver, trip, cancellationToken);
        trace = trace with { Compliance = compliance };
        if (!compliance.ToSnapshot().IsCompliant && !pastCompliance) return new AllocationVerdict(trace, AllocationOutcome.RejectedCompliance);

        OperationTrace.Current?.Begin(TraceStages.Vehicle);
        var eligibility = _vehicles.CheckEligibility(vehicle, trip.ScheduledWindow, trip.RequiredSeats);
        trace = trace with { Eligibility = eligibility };
        if (!eligibility.IsEligible) return new AllocationVerdict(trace, AllocationOutcome.RejectedVehicle);

        OperationTrace.Current?.Begin(TraceStages.DriverOverlap);
        var driverConflict = await _schedule.CheckDriverConflict(driver.Id, trip.ScheduledWindow, trip.Id, cancellationToken);
        trace = trace with { DriverConflict = driverConflict };
        if (driverConflict.HasConflict) return new AllocationVerdict(trace, AllocationOutcome.RejectedSchedule);

        OperationTrace.Current?.Begin(TraceStages.VehicleOverlap);
        var vehicleConflict = await _schedule.CheckVehicleConflict(vehicle.Id, trip.ScheduledWindow, trip.Id, cancellationToken);
        trace = trace with { VehicleConflict = vehicleConflict };
        return new AllocationVerdict(trace, vehicleConflict.HasConflict ? AllocationOutcome.RejectedSchedule : null);
    }
}
