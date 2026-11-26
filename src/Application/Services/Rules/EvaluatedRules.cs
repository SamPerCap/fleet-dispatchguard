using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Rules;

/// <summary>
/// Which rules an evaluation reached, passed or not, for the trace of the request (SPEC 0085 R6) and for the X-ray of the answer
/// (SPEC 0097). Only reads the verdict.
/// </summary>
internal static class EvaluatedRules
{
    private const string DriverAvailable = "driver-available";
    private const string VehicleSuitable = "vehicle-suitable";

    public static void AddTo(OperationTrace operation, AllocationTrace trace)
    {
        operation.Evaluated(DriverAvailable, trace.Driver.IsEligible);
        foreach (var check in trace.Compliance?.RuleChecks ?? [])
        {
            operation.Evaluated(AttemptRules.CatalogId(check.RuleId) ?? check.RuleId, check.Outcome != RuleCheckOutcome.Violated);
        }

        if (trace.Compliance?.LaterServices is { } later) operation.Evaluated(AttemptRules.LaterServices, !later.HasImpacts);
        if (trace.Eligibility is { } eligibility) operation.Evaluated(VehicleSuitable, eligibility.IsEligible);
        if (trace.DriverConflict is { } driver) operation.Evaluated(AttemptRules.DriverOverlap, !driver.HasConflict);
        if (trace.VehicleConflict is { } vehicle) operation.Evaluated(AttemptRules.VehicleOverlap, !vehicle.HasConflict);
    }

    /// <summary>
    /// Every step of <see cref="AllocationEvaluator"/>, in its order, with the ones after a rejection as not reached. A CE 561/2006
    /// failure that did not stop the evaluation was overridden (SPEC 0056).
    /// </summary>
    public static IReadOnlyList<TracedCheck> ChecksOf(AllocationVerdict verdict)
    {
        var trace = verdict.Trace;
        var compliance = trace.Compliance is not { } legal
            ? NotReached(TraceStages.Compliance)
            : new TracedCheck(
                TraceStages.Compliance,
                legal.ToSnapshot().IsCompliant ? CheckOutcomes.Passed
                    : verdict.Rejection == AllocationOutcome.RejectedCompliance ? CheckOutcomes.Rejected : CheckOutcomes.Overridden,
                FiguresOf(legal));

        return
        [
            new TracedCheck(TraceStages.DriverAvailable, OutcomeOf(trace.Driver.IsEligible), Violated(trace.Driver.Violations)),
            compliance,
            trace.Eligibility is { } vehicle
                ? new TracedCheck(TraceStages.Vehicle, OutcomeOf(vehicle.IsEligible), Violated(vehicle.Violations))
                : NotReached(TraceStages.Vehicle),
            trace.DriverConflict is { } driver
                ? new TracedCheck(TraceStages.DriverOverlap, OutcomeOf(!driver.HasConflict), [])
                : NotReached(TraceStages.DriverOverlap),
            trace.VehicleConflict is { } other
                ? new TracedCheck(TraceStages.VehicleOverlap, OutcomeOf(!other.HasConflict), [])
                : NotReached(TraceStages.VehicleOverlap)
        ];
    }

    private static string OutcomeOf(bool passed) => passed ? CheckOutcomes.Passed : CheckOutcomes.Rejected;

    private static TracedCheck NotReached(string step) => new(step, CheckOutcomes.NotReached, []);

    private static IReadOnlyList<TracedRuleFigure> Violated(IEnumerable<ComplianceViolation> violations) =>
        [.. violations.Select(v => new TracedRuleFigure(v.RuleId, null, false, null, null, null))];

    private static IReadOnlyList<TracedRuleFigure> FiguresOf(ComplianceCheckResult compliance)
    {
        List<TracedRuleFigure> figures =
        [
            .. compliance.RuleChecks.Select(check => new TracedRuleFigure(
                check.RuleId, check.Article, check.Outcome != RuleCheckOutcome.Violated, check.Actual, check.Limit, UnitOf(check.Unit), check.Name))
        ];
        if (compliance.LaterServices is { } later)
        {
            figures.Add(new TracedRuleFigure(AttemptRules.LaterServices, null, !later.HasImpacts, null, null, null));
        }

        return figures;
    }

    private static string UnitOf(RuleUnit unit) => unit == RuleUnit.Rests ? "rests" : "hours";
}
