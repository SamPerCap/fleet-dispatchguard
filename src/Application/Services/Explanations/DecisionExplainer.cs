using System.Globalization;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Explanations;

/// <summary>
/// What <see cref="DriverAllocationService.AllocateDriver"/> actually computed. A null check means it stopped before running it
/// (fail-fast).
/// </summary>
public sealed record AllocationTrace(
    DriverEligibilityCheck Driver,
    ComplianceCheckResult? Compliance,
    VehicleEligibilityCheck? Eligibility,
    ScheduleConflictCheck? DriverConflict,
    ScheduleConflictCheck? VehicleConflict,
    int RequiredSeats,
    int SeatingCapacity);

/// <summary>
/// Every figure comes from the computed value objects (<see cref="ComplianceRuleCheck"/>, rankings, checks); the only static
/// texts describe code structure, and component names come from <c>nameof</c> so a rename cannot leave them stale (SPEC 0042).
/// </summary>
public static class DecisionExplainer
{

    public const string AdrLegalFirst = "docs/adr/0001-validacion-legal-antes-de-asignar.md";
    public const string AdrSingleSource = "docs/adr/0002-cifras-ce561-en-una-sola-fuente.md";
    public const string AdrHistory = "docs/adr/0003-historial-derivado-de-servicios.md";
    public const string AdrAvailability = "docs/adr/0006-disponibilidad-aproximada.md";
    public const string AdrRanking = "docs/adr/0007-ranking-de-candidatos.md";
    public const string AdrUnverifiable = "docs/adr/0008-descanso-semanal-no-verificable.md";
    public const string AdrApi = "docs/adr/0010-api-minimal-y-problemdetails.md";
    public const string AdrForced = "docs/adr/0015-forzar-asignacion-con-motivo.md";
    public const string AdrLaterServices = "docs/adr/0016-servicios-posteriores-ya-asignados.md";
    public const string AdrDriverAvailable = "docs/adr/0019-conductor-de-baja-o-inactivo.md";
    public const string AdrRecommendation = "docs/adr/0020-recomendar-asignacion.md";

    /// <summary>Every ADR an explanation may cite; a test checks that they exist.</summary>
    public static readonly IReadOnlyList<string> AllAdrs =
        [AdrLegalFirst, AdrSingleSource, AdrHistory, AdrAvailability, AdrRanking, AdrUnverifiable, AdrApi, AdrForced, AdrLaterServices, AdrDriverAvailable];


    private static readonly string AssignStep = $"{nameof(Trip)}.{nameof(Trip.AssignResources)}";

    /// <summary>
    /// The same rule-by-rule items, the failed ones still failed, with a summary and a final step that say it was forced
    /// (SPEC 0056 R6).
    /// </summary>
    public static DecisionExplanation ForForcedAllocation(AllocationTrace trace, AllocationOverride force)
    {
        ArgumentNullException.ThrowIfNull(force);
        var explanation = ForAllocation(trace);
        var failed = explanation.Business.Where(i => i.Outcome == ExplanationOutcome.Failed).Select(i => i.Title).Distinct().ToList();
        var flow = explanation.Flow
            .Where(f => f.Component != nameof(ComplianceException) && f.Component != AssignStep)
            .Select(f => f.Component == nameof(DriverAllocationService)
                ? f with { Result = Texts.ForcedFlowResult() }
                : f)
            .Append(new TechnicalStep($"{nameof(Trip)}.{nameof(Trip.ForceAssign)}",
                Texts.ForceAssignRole(), ExplanationOutcome.Warning, Texts.ForceAssignResult()))
            .ToList();
        return explanation with
        {
            Summary = Texts.ForcedSummary(force.By, failed, force.Reason),
            Flow = flow,
            Adrs = explanation.Adrs.Append(AdrForced).ToList()
        };
    }

    /// <summary>The same checks over the copy of a recommendation: nothing has been assigned nor saved (SPEC 0062).</summary>
    public static DecisionExplanation ForProposal(AllocationTrace trace)
    {
        var explanation = ForAllocation(trace);
        var failed = explanation.Business.Where(i => i.Outcome == ExplanationOutcome.Failed).Select(i => i.Title).Distinct().ToList();
        var warnings = explanation.Business.Count(i => i.Outcome == ExplanationOutcome.Warning);
        var flow = explanation.Flow
            .Where(f => f.Component != nameof(DriverAllocationService) && f.Component != AssignStep)
            .Prepend(new TechnicalStep(nameof(AllocationEvaluator), Texts.ProposalRole(),
                failed.Count == 0 ? ExplanationOutcome.Passed : ExplanationOutcome.Failed,
                failed.Count == 0 ? Texts.AllValidatorsPassed() : Texts.StopsAtFirstFailure()))
            .ToList();
        return explanation with
        {
            Summary = failed.Count > 0 ? Texts.ProposalFails(failed)
                : warnings > 0 ? Texts.ProposalCompliesWithWarnings(RuleCount(trace.Compliance!), warnings)
                : Texts.ProposalComplies(RuleCount(trace.Compliance!)),
            Flow = flow,
            Adrs = explanation.Adrs.Append(AdrRecommendation).ToList()
        };
    }

    public static DecisionExplanation ForAllocation(AllocationTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var compliance = trace.Compliance;
        var business = new List<ExplanationItem>
        {
            trace.Driver.IsEligible
                ? new(Texts.DriverAvailable(), null, ExplanationOutcome.Passed, Texts.DriverIsAvailable())
                : new(Texts.DriverAvailable(), null, ExplanationOutcome.Failed, Listed(trace.Driver.Violations))
        };
        business.AddRange(compliance is null ? [NotEvaluated(Regulation)] : ComplianceItems(compliance));
        var flow = new List<TechnicalStep>();
        var legalOk = compliance?.IsCompliant == true;
        var vehicleOk = trace.Eligibility?.IsEligible == true;
        var driverFree = trace.DriverConflict is { HasConflict: false };
        var vehicleFree = trace.VehicleConflict is { HasConflict: false };
        var allocated = legalOk && vehicleOk && driverFree && vehicleFree;

        business.Add(trace.Eligibility switch
        {
            null => NotEvaluated(Texts.VehicleFit()),
            { IsEligible: true } => new ExplanationItem(Texts.VehicleFit(), null, ExplanationOutcome.Passed,
                Texts.VehicleIsFit(trace.RequiredSeats, trace.SeatingCapacity)),
            var eligibility => new ExplanationItem(Texts.VehicleFit(), null, ExplanationOutcome.Failed, Listed(eligibility.Violations))
        });
        business.Add(ScheduleItem(Texts.NoDriverOverlap(), trace.DriverConflict));
        business.Add(ScheduleItem(Texts.NoVehicleOverlap(), trace.VehicleConflict));

        var failed = business.Where(i => i.Outcome == ExplanationOutcome.Failed).Select(i => i.Title).Distinct().ToList();
        var warnings = business.Count(i => i.Outcome == ExplanationOutcome.Warning);
        var summary = !allocated ? Texts.Rejected(failed)
            : warnings > 0 ? Texts.AllocatedWithWarnings(RuleCount(compliance!), warnings)
            : Texts.Allocated(RuleCount(compliance!));

        flow.Add(new TechnicalStep(nameof(DriverAllocationService), Texts.OrchestratorRole(),
            allocated ? ExplanationOutcome.Passed : ExplanationOutcome.Failed, allocated ? Texts.AllValidatorsPassed() : Texts.StopsAtFirstFailure()));
        flow.Add(Validator(nameof(DriverEligibilityCheck), Texts.DriverEligibilityRole(), trace.Driver.IsEligible));
        if (!trace.Driver.IsEligible)
            flow.Add(Exception(nameof(DriverUnavailableException), 422));

        flow.AddRange(ComplianceSteps(compliance));

        flow.Add(Validator(nameof(VehicleEligibilityService), Texts.VehicleEligibilityRole(nameof(VehicleEligibilityCheck)), trace.Eligibility?.IsEligible));
        if (trace.Eligibility is { IsEligible: false })
            flow.Add(Exception(nameof(VehicleIneligibleException), 422));

        flow.Add(Validator(nameof(ScheduleConflictService), Texts.AsWritten($"{nameof(ScheduleConflictService.CheckDriverConflict)} → {nameof(ScheduleConflictCheck)}"), trace.DriverConflict is null ? null : !trace.DriverConflict.HasConflict));
        if (trace.DriverConflict is { HasConflict: true })
            flow.Add(Exception(nameof(ScheduleConflictException), 409));

        flow.Add(Validator(nameof(ScheduleConflictService), Texts.AsWritten($"{nameof(ScheduleConflictService.CheckVehicleConflict)} → {nameof(ScheduleConflictCheck)}"), trace.VehicleConflict is null ? null : !trace.VehicleConflict.HasConflict));
        if (trace.VehicleConflict is { HasConflict: true })
            flow.Add(Exception(nameof(ScheduleConflictException), 409));

        flow.Add(new TechnicalStep(AssignStep, Texts.AssignRole(),
            allocated ? ExplanationOutcome.Passed : ExplanationOutcome.NotEvaluated,
            allocated ? Texts.Saved() : Texts.NothingPersisted()));

        var adrs = new List<string> { AdrLegalFirst, AdrSingleSource, AdrHistory, AdrApi };
        if (warnings > 0)
            adrs.Add(AdrUnverifiable);
        if (compliance?.LaterServices is not null)
            adrs.Add(AdrLaterServices);
        if (!trace.Driver.IsEligible)
            adrs.Add(AdrDriverAvailable);

        return new DecisionExplanation(summary, business, flow,
        [
            Texts.PatternFailFast(nameof(DriverAllocationService)),
            Texts.PatternPureValidators(),
            Texts.PatternOnlyOrchestratorThrows(),
            Texts.PatternSingleSource()
        ], adrs);
    }

    /// <summary>In the same order as the candidates.</summary>
    public static IReadOnlyList<DecisionExplanation> ForDriverCandidates(IReadOnlyList<DriverCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Select((c, i) =>
        {
            (CandidateProximity, double?)? previous = i == 0 ? null : (candidates[i - 1].Proximity, candidates[i - 1].DistanceKm);
            var position = DriverPositionReason(c, i == 0 ? null : candidates[i - 1], previous);
            var business = new List<ExplanationItem>
            {
                new(Texts.HardFilters(), null, ExplanationOutcome.Passed, Texts.Sentence(Texts.Listed(DriverCandidateSearch.HardFilters))),
                new(Texts.BaseProximity(), "SPEC 0031", ExplanationOutcome.Passed, ProximityText(c.Proximity, c.DistanceKm, isVehicle: false)),
                new(Texts.Position(c.Rank), null, ExplanationOutcome.Passed, position)
            };
            if (c.CoversSameBooking)
                business.Insert(business.Count - 1, new(Texts.SameBooking(), "SPEC 0045", ExplanationOutcome.Passed,
                    Texts.CoversSameBooking(c.SameBookingTripId!.Value)));
            if (c.WonTieBreak && c.HoursSinceWeeklyRest is TimeSpan rested)
                business.Insert(business.Count - 1, new(Texts.TieBreakByHours(), "CE 561/2006 Art. 8.6", ExplanationOutcome.Passed,
                    Texts.WonTieBreak(rested.TotalHours)));
            if (c.Compliance is not null)
                business.InsertRange(1, ComplianceItems(c.Compliance));
            else
                business.AddRange(c.Warnings.Select(w => new ExplanationItem(Texts.WarningTitle(), w.RuleId, ExplanationOutcome.Warning, w.Message)));

            var flow = new List<TechnicalStep>
            {
                new(nameof(DriverCandidateSearch), Texts.CheapFiltersRole(), ExplanationOutcome.Passed, Texts.Passes()),
                new(nameof(ProximityRankingPolicy), Texts.RankingRole(nameof(IDriverRankingPolicy)), ExplanationOutcome.Passed, BandAndDistance(c.Proximity, c.DistanceKm)),
                new(nameof(DriverCandidateSearch), Texts.OrderRole(), ExplanationOutcome.Passed,
                    c.CoversSameBooking
                        ? Texts.PositionSameBooking(c.Rank, $"{nameof(ITripRepository)}.{nameof(ITripRepository.GetByBookingAsync)}")
                        : Texts.Position(c.Rank)),
                new($"{nameof(Driver)}.{nameof(Driver.HasAvailabilityFor)}", AvailabilityRole, ExplanationOutcome.Passed, Texts.FitsWorkingDay()),
                new(nameof(LegalValidationService), Texts.ComplianceRole(nameof(LegalComplianceEvaluation)),
                    c.Compliance is null ? ExplanationOutcome.Passed : Outcome(c.Compliance),
                    c.Compliance is null ? Texts.Complies() : Texts.RulesEvaluated(RuleCount(c.Compliance))),
                new(nameof(DriverCandidate), Texts.CandidateRole(), ExplanationOutcome.Passed, c.Reason)
            };

            var adrs = new List<string> { AdrRanking, AdrAvailability, AdrLegalFirst, AdrSingleSource };
            if (c.Warnings.Count > 0)
                adrs.Add(AdrUnverifiable);

            return new DecisionExplanation(
                c.Warnings.Count > 0
                    ? Texts.DriverCandidateSummaryWithWarnings(c.Rank, c.Driver.FullName, c.Warnings.Count, position)
                    : Texts.DriverCandidateSummary(c.Rank, c.Driver.FullName, position),
                business, flow, RankingPatterns, adrs);
        }).ToList();
    }

    /// <summary>In the same order as the candidates.</summary>
    public static IReadOnlyList<DecisionExplanation> ForVehicleCandidates(IReadOnlyList<VehicleCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Select((c, i) =>
        {
            (CandidateProximity, double?)? previous = i == 0 ? null : (candidates[i - 1].Proximity, candidates[i - 1].DistanceKm);
            var position = PositionReason(c.Rank, previous, c.Proximity, c.DistanceKm);
            var business = new List<ExplanationItem>
            {
                new(Texts.VehicleFit(), null, ExplanationOutcome.Passed, c.RequiredSeats is int required
                    ? Texts.VehicleCandidateFit(required, c.Vehicle.SeatingCapacity)
                    : Texts.VehicleCandidateFitNoSeats(c.Vehicle.SeatingCapacity)),
                new(Texts.CurrentVehiclePosition(), null, ExplanationOutcome.Passed, PositionSourceText(c.PositionSource, c.Reason)),
                new(Texts.ProximityToDeparture(), "SPEC 0031", ExplanationOutcome.Passed, ProximityText(c.Proximity, c.DistanceKm, isVehicle: true)),
                new(Texts.Position(c.Rank), null, ExplanationOutcome.Passed, position)
            };
            var flow = new List<TechnicalStep>
            {
                new(nameof(VehicleEligibilityService), Texts.VehicleEligibilityRole(nameof(VehicleEligibilityCheck)), ExplanationOutcome.Passed, Texts.FitResult()),
                new(nameof(VehicleCandidateSearch), Texts.LocateVehicleRole(), ExplanationOutcome.Passed, Texts.AsWritten(c.PositionSource.ToString())),
                new($"{nameof(ProximityRankingPolicy)}.{nameof(ProximityRankingPolicy.RadiusBands)}", Texts.SameBandsRole(), ExplanationOutcome.Passed,
                    BandAndDistance(c.Proximity, c.DistanceKm)),
                new(nameof(VehicleCandidate), Texts.CandidateRole(), ExplanationOutcome.Passed, Texts.Position(c.Rank))
            };
            return new DecisionExplanation(
                Texts.VehicleCandidateSummary(c.Rank, c.Vehicle.LicensePlate, position),
                business, flow, RankingPatterns, [AdrRanking]);
        }).ToList();
    }

    /// <summary>Compared with the candidate just above it.</summary>
    public static LocalizedText PositionReason(int rank, (CandidateProximity Proximity, double? DistanceKm)? previous, CandidateProximity proximity, double? distanceKm)
    {
        if (previous is not { } prev)
            return Texts.FirstByBand(BandName(proximity), Km(distanceKm));
        if (prev.Proximity < proximity)
            return Texts.WorseBand(BandName(proximity), rank - 1, BandName(prev.Proximity));
        if (prev.DistanceKm != distanceKm && distanceKm is not null)
            return Texts.LongerDistance(rank - 1, Km(distanceKm), Km(prev.DistanceKm));
        return Texts.TiedByIdentifier(rank - 1);
    }

    /// <summary>Takes the same-booking preference into account (SPEC 0045 R4).</summary>
    private static LocalizedText DriverPositionReason(DriverCandidate candidate, DriverCandidate? prev, (CandidateProximity, double?)? previous)
    {
        if (candidate.CoversSameBooking && prev is null)
            return Texts.FirstBySameBooking(candidate.SameBookingTripId!.Value);
        if (!candidate.CoversSameBooking && prev is { CoversSameBooking: true })
            return Texts.PreviousCoversBooking(candidate.Rank - 1);

        // A tie on booking, band and distance is decided by hours before falling back to the id (SPEC 0047 R2).
        var reason = prev is not null && prev.Proximity == candidate.Proximity && prev.DistanceKm == candidate.DistanceKm
            && prev.HoursSinceWeeklyRest is TimeSpan before && candidate.HoursSinceWeeklyRest is TimeSpan own && before != own
            ? Texts.TiedByHours(candidate.Rank - 1, own.TotalHours, before.TotalHours)
            : PositionReason(candidate.Rank, previous, candidate.Proximity, candidate.DistanceKm);
        return candidate.CoversSameBooking ? Texts.SameBookingAs(candidate.Rank - 1, reason) : reason;
    }

    /// <summary>24h minus the normal or reduced daily rest (SPEC 0030).</summary>
    private static LocalizedText AvailabilityRole => Texts.AvailabilityRole(
        (DrivingLimits.DailyRestWindow - DrivingLimits.DailyRestNormal).TotalHours,
        (DrivingLimits.DailyRestWindow - DrivingLimits.DailyRestReduced).TotalHours);

    private static readonly IReadOnlyList<LocalizedText> RankingPatterns =
    [
        Texts.PatternFiltersFirst(),
        Texts.PatternRankingPolicy(nameof(IDriverRankingPolicy), nameof(ProximityRankingPolicy)),
        Texts.PatternLazyEvaluation(),
        Texts.PatternImmutableResults()
    ];

    private static LocalizedText Regulation => Texts.AsWritten("CE 561/2006");

    private static LocalizedText Listed(IEnumerable<ComplianceViolation> violations) => Texts.Listed(violations.Select(v => v.Message).ToList());

    private static LocalizedText BandAndDistance(CandidateProximity proximity, double? km) => Texts.BandAndDistance(proximity.ToString(), Km(km));

    /// <summary>Nothing when the later services were not evaluated.</summary>
    private static IEnumerable<ExplanationItem> LaterServicesItems(LaterServicesCheck? later)
    {
        if (later is null) return [];
        if (later.HasImpacts)
            return later.Impacts.Select(i => new ExplanationItem(Texts.LaterServices(), $"CE 561/2006 {i.With.Article}", ExplanationOutcome.Failed, Texts.Sentence(i.Message)));
        return [new ExplanationItem(Texts.LaterServices(), "SPEC 0063", ExplanationOutcome.Passed, later.Evaluated == 0
            ? Texts.NoLaterServices(later.Until)
            : Texts.LaterServicesUnharmed(later.Evaluated, later.Until))];
    }

    private static IEnumerable<ExplanationItem> ComplianceItems(ComplianceCheckResult compliance) =>
        OwnRuleItems(compliance).Concat(LaterServicesItems(compliance.LaterServices));

    private static IEnumerable<ExplanationItem> OwnRuleItems(ComplianceCheckResult compliance)
    {
        if (compliance.RuleChecks.Count == 0)
            return compliance.Violations.Where(v => !v.RuleId.StartsWith(LaterServiceImpact.RuleIdPrefix, StringComparison.Ordinal)).Select(v => new ExplanationItem(Regulation, v.RuleId,
                v.Severity == Core.Enums.ViolationSeverity.Warning ? ExplanationOutcome.Warning : ExplanationOutcome.Failed, v.Message));

        return compliance.RuleChecks.Select(c => new ExplanationItem(c.Name, c.Article, c.Outcome switch
        {
            RuleCheckOutcome.Passed => ExplanationOutcome.Passed,
            RuleCheckOutcome.Warning => ExplanationOutcome.Warning,
            _ => ExplanationOutcome.Failed
        }, RuleDetail(c)));
    }

    private static LocalizedText RuleDetail(ComplianceRuleCheck check)
    {
        var text = (check.Actual, check.Limit) switch
        {
            (null, _) => null,
            (double actual, double limit) when check.Unit == RuleUnit.Rests => Texts.RestsOf(actual, limit),
            (double actual, double limit) when check.RuleId.Contains("DailyRest") => Texts.RestThatFits(actual, limit, DrivingLimits.DailyRestReduced.TotalHours),
            (double actual, double limit) when check.RuleId.Contains("WeeklyRest") => Texts.SinceWeeklyRest(actual, limit),
            (double actual, double limit) => Texts.HoursOf(actual, limit),
            _ => null
        };
        return Texts.Sentence(Texts.Sentences(new[] { text, check.Note }.OfType<LocalizedText>().ToList()));
    }

    private static int RuleCount(ComplianceCheckResult compliance) => compliance.RuleChecks.Count;

    private static ExplanationOutcome Outcome(ComplianceCheckResult compliance) =>
        !compliance.IsCompliant ? ExplanationOutcome.Failed
        : compliance.RuleChecks.Any(r => r.Outcome == RuleCheckOutcome.Warning) || compliance.Violations.Count > 0 ? ExplanationOutcome.Warning
        : ExplanationOutcome.Passed;

    private static IEnumerable<TechnicalStep> ComplianceSteps(ComplianceCheckResult? compliance)
    {
        var load = Texts.LoadHistoryRole();
        var engine = Texts.EngineRole();
        if (compliance is null)
        {
            yield return Validator(nameof(LegalValidationService), load, null);
            yield return Validator(nameof(LegalComplianceEvaluation), engine, null);
            yield break;
        }

        yield return new TechnicalStep(nameof(LegalValidationService), load,
            ExplanationOutcome.Passed, Texts.AsWritten($"{nameof(ILegalValidationService.ValidateDriverForTripDetailed)} → {nameof(ComplianceCheckResult)}"));
        yield return new TechnicalStep(nameof(LegalComplianceEvaluation), engine,
            OwnRulesOutcome(compliance),
            Texts.EngineResult(RuleCount(compliance), compliance.RuleChecks.Count(c => c.Outcome == RuleCheckOutcome.Violated),
                compliance.RuleChecks.Count(c => c.Outcome == RuleCheckOutcome.Warning)));
        if (compliance.LaterServices is { } later)
            yield return new TechnicalStep(nameof(LaterServicesEvaluation), Texts.LaterServicesRole(nameof(LegalComplianceEvaluation)),
                later.HasImpacts ? ExplanationOutcome.Failed : ExplanationOutcome.Passed,
                Texts.LaterServicesResult(later.Evaluated, later.AffectedTripIds.Count));
        if (!compliance.IsCompliant)
            yield return Exception(nameof(ComplianceException), 422);
    }

    /// <summary>Verdict of the trip's own rules, without its later services (SPEC 0063).</summary>
    private static ExplanationOutcome OwnRulesOutcome(ComplianceCheckResult compliance) =>
        compliance.RuleChecks.Count == 0 ? Outcome(compliance)
        : compliance.RuleChecks.Any(r => r.Outcome == RuleCheckOutcome.Violated) ? ExplanationOutcome.Failed
        : compliance.RuleChecks.Any(r => r.Outcome == RuleCheckOutcome.Warning) ? ExplanationOutcome.Warning
        : ExplanationOutcome.Passed;

    private static ExplanationItem ScheduleItem(LocalizedText title, ScheduleConflictCheck? check) => check switch
    {
        null => NotEvaluated(title),
        { HasConflict: false } => new ExplanationItem(title, null, ExplanationOutcome.Passed, Texts.NothingOverlaps()),
        _ => new ExplanationItem(title, null, ExplanationOutcome.Failed,
            Texts.Overlaps(string.Join(", ", check.ConflictingTripIds.Select(id => $"#{id}"))))
    };

    private static ExplanationItem NotEvaluated(LocalizedText title) =>
        new(title, null, ExplanationOutcome.NotEvaluated, Texts.NotCheckedAfterFailure());

    private static TechnicalStep Validator(string component, LocalizedText role, bool? passed) => passed switch
    {
        null => new TechnicalStep(component, role, ExplanationOutcome.NotEvaluated, Texts.NotRunFailFast()),
        true => new TechnicalStep(component, role, ExplanationOutcome.Passed, Texts.Ok()),
        false => new TechnicalStep(component, role, ExplanationOutcome.Failed, Texts.NegativeResult())
    };

    private static TechnicalStep Exception(string name, int status) =>
        new(name, Texts.DomainExceptionRole(), ExplanationOutcome.Failed, Texts.HttpNothingPersisted(status));

    private static LocalizedText ProximityText(CandidateProximity proximity, double? km, bool isVehicle)
    {
        var subject = isVehicle ? Texts.VehiclePositionSubject() : Texts.DriverBaseSubject();
        return proximity switch
        {
            CandidateProximity.BaseAtOriginAndDestination => isVehicle
                ? Texts.PositionIsOriginAndBaseIsDestination()
                : Texts.BaseIsOriginAndDestination(),
            CandidateProximity.BaseAtOrigin => Texts.SubjectAtOrigin(subject),
            CandidateProximity.Beyond40Km => Texts.SubjectBeyondRadius(subject, Km(km), ProximityRankingPolicy.RadiusBands[^1].MaxKm),
            CandidateProximity.NoHomeLocation => isVehicle ? Texts.VehiclePositionUnknown() : Texts.DriverWithoutBase(),
            _ => Texts.SubjectWithinBand(subject, Km(km), BandName(proximity))
        };
    }

    private static LocalizedText BandName(CandidateProximity proximity) => proximity switch
    {
        CandidateProximity.BaseAtOriginAndDestination => Texts.BandOriginAndDestination(),
        CandidateProximity.BaseAtOrigin => Texts.BandOrigin(),
        CandidateProximity.Beyond40Km => Texts.BandBeyond(),
        CandidateProximity.NoHomeLocation => Texts.BandNoPosition(),
        _ => Texts.BandRadius(ProximityRankingPolicy.RadiusBands.First(b => b.Proximity == proximity).MaxKm)
    };

    private static LocalizedText PositionSourceText(VehiclePositionSource source, LocalizedText reason) => source switch
    {
        VehiclePositionSource.LastTripDestination => Texts.LastServiceDestination(reason),
        VehiclePositionSource.HomeBase => Texts.NoRecentServices(reason),
        _ => Texts.PositionUnknown(reason)
    };

    private static LocalizedText Km(double? km) => km is double distance ? Texts.Kilometres(distance) : Texts.UnknownDistance();
}
