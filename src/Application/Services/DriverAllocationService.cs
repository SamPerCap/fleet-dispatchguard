using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using System.Linq;

namespace DispatchGuard.Application.Services;

/// <summary>Carries the compliance snapshot that justified the allocation, for audit.</summary>
public sealed record AllocationResult(Driver Driver, Vehicle Vehicle, LegalValidation Validation, DecisionExplanation Explanation)
{
    /// <summary>Assigned past a CE 561/2006 rejection by explicit human decision (SPEC 0056).</summary>
    public bool Forced { get; init; }

    /// <summary>Later services of the driver left illegal and marked by a forced assignment (SPEC 0063 R8).</summary>
    public IReadOnlyList<int> AffectedLaterTripIds { get; init; } = [];
}

public interface IDriverAllocationService
{
    /// <summary>
    /// Reading, validating and saving are one exclusive step in the database per driver, vehicle and service (SPEC 0065, ADR 0018):
    /// that is why it takes ids and loads everything itself. With any of the exceptions below, no allocation is made.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Unknown trip, driver or vehicle.</exception>
    /// <exception cref="TripNotAllocatableException">The trip is cancelled or locked, or not to be recommended with <paramref name="onlyIfUnassigned"/>.</exception>
    /// <exception cref="ResourceBusyException">
    /// Another operation kept the same driver, vehicle or service for too long: nothing is saved nor recorded, try again.
    /// </exception>
    /// <exception cref="DriverUnavailableException">Inactive or with an unavailability during the trip; forced or not (SPEC 0066).</exception>
    /// <exception cref="ComplianceException">The driver is not legally allowed to perform the trip (CE 561/2006).</exception>
    /// <exception cref="VehicleIneligibleException">Inactive or decommissioned, insufficient capacity, or overlapping unavailability.</exception>
    /// <exception cref="ScheduleConflictException">The driver or the vehicle has another active trip that overlaps.</exception>
    /// <exception cref="AttemptNotRecordedException">The attempt, accepted or rejected, could not be recorded (SPEC 0085 R4).</exception>
    /// <param name="attemptedBy">Recorded in the attempt log.</param>
    /// <param name="force">
    /// Explicit human override of a CE 561/2006 rejection (SPEC 0056). The engine still validates everything;
    /// only the compliance step may be overridden, never driver availability, vehicle eligibility or schedule conflicts (R1).
    /// When the trip turns out legal the override is ignored (R3).
    /// </param>
    /// <param name="origin">Whether a person chose the pair or applied a recommended one.</param>
    /// <param name="onlyIfUnassigned">Reject when the trip is no longer one to recommend: it got resources meanwhile, departed… (SPEC 0062 R1, R10).</param>
    Task<AllocationResult> AllocateDriver(
        int tripId,
        int driverId,
        int vehicleId,
        CancellationToken cancellationToken = default,
        string? attemptedBy = null,
        AllocationOverride? force = null,
        AttemptOrigin origin = AttemptOrigin.Manual,
        bool onlyIfUnassigned = false);
}

/// <summary>Legality is enforced BEFORE allocating, never after; the assignment itself belongs to the <see cref="Trip"/> aggregate.</summary>
public class DriverAllocationService : IDriverAllocationService
{
    private readonly AllocationEvaluator _evaluator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAllocationAttemptLog? _attemptLog;
    private readonly IForcedConsequences _consequences;

    public DriverAllocationService(
        ILegalValidationService legalValidationService,
        IVehicleEligibilityService vehicleEligibilityService,
        IScheduleConflictService scheduleConflictService,
        IUnitOfWork unitOfWork,
        IAllocationAttemptLog? attemptLog = null,
        IForcedConsequences? consequences = null)
    {
        _attemptLog = attemptLog;
        _evaluator = new AllocationEvaluator(legalValidationService, vehicleEligibilityService, scheduleConflictService);
        _unitOfWork = unitOfWork
            ?? throw new ArgumentNullException(nameof(unitOfWork));
        _consequences = consequences ?? new ForcedConsequences(_unitOfWork, legalValidationService);
    }

    public async Task<AllocationResult> AllocateDriver(
        int tripId,
        int driverId,
        int vehicleId,
        CancellationToken cancellationToken = default,
        string? attemptedBy = null,
        AllocationOverride? force = null,
        AttemptOrigin origin = AttemptOrigin.Manual,
        bool onlyIfUnassigned = false)
    {
        // A recommendation is never forced (SPEC 0062 R1).
        if (force is not null && origin == AttemptOrigin.Recommended)
        {
            throw new InvalidRequestException(Texts.RecommendationNotForcible());
        }

        AllocationResult result;
        var steps = new TraceHolder();
        try
        {
            (result, _) = await _unitOfWork.ExecuteExclusiveAsync(
                ResourceClaim.Allocation(tripId, driverId, vehicleId),
                async ct =>
                {
                    steps.IsBeingRecorded = false;
                    OperationTrace.Current?.Begin(TraceStages.Read);
                    var trip = await _unitOfWork.Trips.GetByIdAsync(tripId, ct) ?? throw MissingResource.Trip(tripId);
                    var driver = await _unitOfWork.Drivers.GetByIdAsync(driverId, ct) ?? throw MissingResource.Driver(driverId);
                    var vehicle = await _unitOfWork.Vehicles.GetByIdAsync(vehicleId, ct) ?? throw MissingResource.Vehicle(vehicleId);
                    // Whoever assigned it since the proposal was calculated keeps it (SPEC 0062 R10).
                    if (onlyIfUnassigned && trip.WhyNotRecommendable(DateTime.UtcNow) is { } reason)
                    {
                        throw new TripNotAllocatableException(reason);
                    }

                    if (!trip.IsActive || trip.IsLocked)
                    {
                        throw new TripNotAllocatableException(!trip.IsActive ? Texts.ServiceCancelled(tripId) : Texts.ServiceLocked(tripId));
                    }

                    steps.BaseId = trip.BaseId;
                    var allocated = await AllocateCore(driver, vehicle, trip, force, steps, ct);
                    // In the transaction of the assignment: one without its record is not made (SPEC 0085 R4).
                    await RecordAllocatedAsync(allocated, ct);
                    return (allocated, trip);
                },
                done => done.trip,
                cancellationToken);
        }
        catch (Exception ex) when (OutcomeOf(ex) is AllocationOutcome rejected)
        {
            // The log keeps what the dispatcher read, not the exception's technical message.
            var explanation = ExplanationOf(ex);
            try
            {
                // Even if whoever asked has left.
                await RecordAsync(rejected, RejectionDetail(explanation) ?? (ex as IHasLocalizedText)?.Text, explanation, attemptedBy, null, CancellationToken.None);
                OperationTrace.Current?.End();
            }
            catch (Exception failure)
            {
                throw new AttemptNotRecordedException(tripId, failure);
            }

            throw;
        }
        catch (Exception ex) when (steps.IsBeingRecorded && ex is not OperationCanceledException)
        {
            throw new AttemptNotRecordedException(tripId, ex);
        }

        return result;

        async Task RecordAllocatedAsync(AllocationResult allocated, CancellationToken token)
        {
            // Not wrapped here: inside the transaction, the kind of failure decides whether it is tried again.
            steps.IsBeingRecorded = true;
            if (allocated.Forced)
            {
                // The record of a forced assignment says who forced it, why, which rules were skipped (SPEC 0056 R5) and which later
                // services it makes illegal (SPEC 0063 R9).
                var broken = Texts.Listed(allocated.Validation.Violations.ToList());
                var affected = string.Join(", ", allocated.AffectedLaterTripIds.Select(id => $"#{id}"));
                await RecordAsync(AllocationOutcome.Forced,
                    affected.Length == 0 ? Texts.ForcedDetail(force!.Reason, broken) : Texts.ForcedDetailWithAffected(force!.Reason, affected, broken),
                    allocated.Explanation, force.By, force.Reason, token);
            }
            else
            {
                await RecordAsync(AllocationOutcome.Assigned, null, allocated.Explanation, attemptedBy, null, token);
            }

            steps.IsBeingRecorded = false;
        }

        // Every human attempt leaves a record (SPEC 0050 R4), with what is needed to audit it as it was decided (SPEC 0059).
        async Task RecordAsync(
            AllocationOutcome outcome, LocalizedText? detail, DecisionExplanation? explanation, string? by, string? forcedReason, CancellationToken token)
        {
            if (_attemptLog is null) return;
            OperationTrace.Current?.Begin(TraceStages.Record);
            IReadOnlyList<string> rules = steps.Trace is { } trace ? AttemptRules.Of(trace) : [];
            await _attemptLog.RecordAsync(new AllocationAttempt(
                DateTime.UtcNow, by, tripId, driverId, vehicleId, steps.BaseId, outcome, detail?.In(AttemptTexts.StoredLanguage),
                rules, forcedReason, explanation is null ? null : AttemptTexts.Serialize(explanation, detail), origin), token);
        }
    }

    /// <summary>The failed checks as the dispatcher reads them in the explanation.</summary>
    internal static LocalizedText? RejectionDetail(DecisionExplanation? explanation)
    {
        if (explanation is null) return null;
        var failed = explanation.Business.Where(i => i.Outcome == ExplanationOutcome.Failed).Select(i => Texts.Titled(i.Title, i.Detail)).ToList();
        return failed.Count > 0 ? Texts.Dotted(failed) : explanation.Summary;
    }

    private static DecisionExplanation? ExplanationOf(Exception ex) => ex switch
    {
        DriverUnavailableException unavailable => unavailable.Explanation,
        ComplianceException illegal => illegal.Explanation,
        VehicleIneligibleException ineligible => ineligible.Explanation,
        ScheduleConflictException conflict => conflict.Explanation,
        _ => null
    };

    private static AllocationOutcome? OutcomeOf(Exception ex) => ex switch
    {
        DriverUnavailableException => AllocationOutcome.RejectedDriver,
        ComplianceException => AllocationOutcome.RejectedCompliance,
        VehicleIneligibleException => AllocationOutcome.RejectedVehicle,
        ScheduleConflictException => AllocationOutcome.RejectedSchedule,
        _ => null
    };

    private async Task<AllocationResult> AllocateCore(
        Driver driver,
        Vehicle vehicle,
        Trip trip,
        AllocationOverride? force,
        TraceHolder steps,
        CancellationToken cancellationToken)
    {
        // Everything is validated BEFORE allocating, by the same steps that a recommendation simulates (SPEC 0062). Only the
        // compliance step can be overridden, and only by an explicit, validated override (SPEC 0056).
        var verdict = await _evaluator.EvaluateAsync(driver, vehicle, trip, pastCompliance: force is not null, cancellationToken);
        var trace = steps.Trace = verdict.Trace;
        if (verdict.Rejection is { } rejection)
        {
            throw RejectionOf(rejection, trace);
        }

        OperationTrace.Current?.Begin(TraceStages.Assign);
        var compliance = trace.Compliance!;
        var validation = compliance.ToSnapshot();
        var forced = !validation.IsCompliant;

        // The marks this trip left on later services go with its driver (SPEC 0063 R8).
        var sameDriver = trip.AssignedDriverId == driver.Id;
        IReadOnlyList<Trip> released = trip.AssignedDriverId is not null && !sameDriver
            ? await _consequences.ReleaseAsync([trip.Id], cancellationToken)
            : [];

        if (forced)
        {
            trip.ForceAssign(driver, vehicle, validation, force!);
            if (sameDriver)
            {
                await _consequences.RenewAsync(trip, force!, cancellationToken);
            }

            if (compliance.LaterServices is { HasImpacts: true } later)
            {
                await _consequences.MarkAsync(trip, later, force!, cancellationToken);
            }
        }
        else if (sameDriver && trip.ForcedReason is not null
            && (await _unitOfWork.Trips.GetForcedByTripsAsync([trip.Id], cancellationToken)).Count > 0)
        {
            trip.ReassignKeepingForced(driver, vehicle);
        }
        else
        {
            trip.AssignResources(driver, vehicle);
            trip.SetLegalValidation(validation);
        }

        await _unitOfWork.Trips.UpdateAsync(trip, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _consequences.RevalidateAsync(released, cancellationToken);

        var explanation = forced ? DecisionExplainer.ForForcedAllocation(trace, force!) : DecisionExplainer.ForAllocation(trace);
        return new AllocationResult(driver, vehicle, validation, explanation)
        {
            Forced = forced,
            AffectedLaterTripIds = forced ? compliance.LaterServices?.AffectedTripIds ?? [] : []
        };
    }

    private static Exception RejectionOf(AllocationOutcome rejection, AllocationTrace trace)
    {
        var explanation = DecisionExplainer.ForAllocation(trace);
        return rejection switch
        {
            AllocationOutcome.RejectedDriver => new DriverUnavailableException(trace.Driver.Violations.Select(v => v.Message)) { Explanation = explanation },
            AllocationOutcome.RejectedCompliance => new ComplianceException(trace.Compliance!.ToSnapshot().Violations)
            {
                Explanation = explanation,
                LaterServices = trace.Compliance.LaterServices?.Impacts ?? []
            },
            AllocationOutcome.RejectedVehicle => new VehicleIneligibleException(trace.Eligibility!.Violations.Select(v => v.Message)) { Explanation = explanation },
            _ => new ScheduleConflictException((trace.VehicleConflict ?? trace.DriverConflict)!.ConflictingTripIds) { Explanation = explanation }
        };
    }

    /// <summary>What the engine computed so far, still readable after a rejection.</summary>
    private sealed class TraceHolder
    {
        public AllocationTrace? Trace { get; set; }

        public int BaseId { get; set; }

        /// <summary>What fails while it is set is the record, not the assignment.</summary>
        public bool IsBeingRecorded { get; set; }
    }
}
