using System.Reflection;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

/// <summary>Explanations are built from what the engine computed, in the order the code ran (SPEC 0042).</summary>
public class DecisionExplainerTests
{
    private static readonly DateTime Day = new(2026, 11, 16);

    // 06:00–10:00 driven, trip at 11:00 (1h break → continuous restarts at 0).
    private static ComplianceCheckResult Compliance(double tripHours) =>
        LegalComplianceEvaluation.Validate([new DrivingPeriod(1, Day.AddHours(6), Day.AddHours(10))], Day.AddHours(11), TimeSpan.FromHours(tripHours));

    private static readonly TimeWindow Window = new(Day.AddHours(11), Day.AddHours(13));

    private static DriverEligibilityCheck Available => DriverEligibilityCheck.For(new Driver("1", "Ana", "Gil"), Window);

    private static DriverEligibilityCheck OnLeave()
    {
        var driver = new Driver("1", "Ana", "Gil");
        driver.AddUnavailability(new DriverUnavailability(1, Window, UnavailabilityType.SickLeave));
        return DriverEligibilityCheck.For(driver, Window);
    }

    private static AllocationTrace Trace(
        ComplianceCheckResult compliance,
        VehicleEligibilityCheck? eligibility = null,
        ScheduleConflictCheck? driverConflict = null,
        ScheduleConflictCheck? vehicleConflict = null) =>
        new(Available, compliance, eligibility, driverConflict, vehicleConflict, RequiredSeats: 40, SeatingCapacity: 55);

    [Fact]
    public void ForAllocation_AllPassed_FlowFollowsOrchestratorOrder()
    {
        var explanation = DecisionExplainer.ForAllocation(Trace(
            Compliance(2), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.NoConflict()));

        Assert.Equal(
            [nameof(DriverAllocationService), nameof(DriverEligibilityCheck), nameof(LegalValidationService), nameof(LegalComplianceEvaluation),
             nameof(VehicleEligibilityService), nameof(ScheduleConflictService), nameof(ScheduleConflictService), "Trip.AssignResources"],
            explanation.Flow.Select(s => s.Component).ToArray());
        Assert.DoesNotContain(explanation.Flow, s => s.Outcome is ExplanationOutcome.Failed or ExplanationOutcome.NotEvaluated);
        Assert.Contains(explanation.Business, i => i.Reference == "Art. 6.1" && i.Detail.Es().Contains("6,0 h de 10,0 h"));
        Assert.Contains(explanation.Business, i => i.Detail.Es().Contains("40 plazas") && i.Detail.Es().Contains("55"));
    }

    // Trip 10:00–12:00, later service #20 at 12:00 (4 h, two natural weeks of horizon).
    private static ComplianceCheckResult WithLater(params DrivingPeriod[] later) =>
        LegalComplianceEvaluation.Validate(later, Day.AddHours(10), TimeSpan.FromHours(2))
            .WithLaterServices(LaterServicesEvaluation.Evaluate(later, new DrivingPeriod(1, Day.AddHours(10), Day.AddHours(12), 10)));

    [Fact]
    public void ForAllocation_LaterServiceBroken_SaysServiceFiguresAndThatTheOwnRulesPass()
    {
        var explanation = DecisionExplainer.ForAllocation(Trace(WithLater(new DrivingPeriod(1, Day.AddHours(12), Day.AddHours(16), 20))));

        var item = Assert.Single(explanation.Business, i => i.Title.Es() == "Servicios posteriores ya asignados");
        Assert.Equal(ExplanationOutcome.Failed, item.Outcome);
        Assert.Equal("CE 561/2006 Art. 7", item.Reference);
        Assert.Contains("Servicio #20 (16/11 13:00), Art. 7", item.Detail.Es());
        Assert.Contains("6 h con este servicio, 4 h sin él (máximo 4,5 h)", item.Detail.Es());
        Assert.Contains("falla «Servicios posteriores ya asignados»", explanation.Summary.Es());
        // The trip's own rules were not the ones that failed.
        Assert.NotEqual(ExplanationOutcome.Failed, Assert.Single(explanation.Flow, s => s.Component == nameof(LegalComplianceEvaluation)).Outcome);
        Assert.Equal(ExplanationOutcome.Failed, Assert.Single(explanation.Flow, s => s.Component == nameof(LaterServicesEvaluation)).Outcome);
        Assert.Contains(DecisionExplainer.AdrLaterServices, explanation.Adrs);
    }

    [Theory]
    [InlineData(false, "0 servicios posteriores asignados hasta el 30/11 14:00.")]
    [InlineData(true, "1 servicio posterior evaluado hasta el 30/11 14:00: ninguno pasa a incumplir ni empeora.")]
    public void ForAllocation_LaterServicesFine_SaysHowManyAndUntilWhen(bool withLater, string expected)
    {
        var explanation = DecisionExplainer.ForAllocation(Trace(WithLater(
            withLater ? [new DrivingPeriod(1, Day.AddHours(14), Day.AddHours(16), 20)] : [])));

        var item = Assert.Single(explanation.Business, i => i.Title.Es() == "Servicios posteriores ya asignados");
        Assert.Equal((ExplanationOutcome.Passed, expected), (item.Outcome, item.Detail.Es()));
    }

    [Fact]
    public void ForAllocation_LaterServicesNotEvaluated_SaysNothingAboutThem() =>
        Assert.DoesNotContain(DecisionExplainer.ForAllocation(Trace(Compliance(2))).Business, i => i.Title.Es() == "Servicios posteriores ya asignados");

    [Fact]
    public void ForForcedAllocation_SeveralLaterServices_NamesTheRuleOnce()
    {
        var trace = Trace(WithLater(
            new DrivingPeriod(1, Day.AddHours(12), Day.AddHours(16), 20), new DrivingPeriod(1, Day.AddHours(16).AddMinutes(10), Day.AddHours(17), 21)),
            VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.NoConflict());

        var explanation = DecisionExplainer.ForForcedAllocation(trace, AllocationOverride.Create("Sin otro conductor", "Ana"));

        Assert.Contains("incumple «Servicios posteriores ya asignados». Motivo", explanation.Summary.Es());
    }

    /// <summary>Rejected before CE 561/2006 is even evaluated (SPEC 0066).</summary>
    [Fact]
    public void ForAllocation_DriverUnavailable_IsTheOnlyFailureAndNothingElseIsEvaluated()
    {
        var explanation = DecisionExplainer.ForAllocation(new AllocationTrace(OnLeave(), null, null, null, null, 40, 55));

        Assert.Equal("Asignación rechazada antes de guardar nada: falla «Conductor disponible».", explanation.Summary.Es());
        Assert.Equal(["Conductor disponible", "CE 561/2006", "Vehículo apto", "Sin solape con otros servicios del conductor", "Sin solape con otros servicios del vehículo"],
            explanation.Business.Select(i => i.Title).Es());
        Assert.StartsWith("Conductor no disponible (baja médica) del ", explanation.Business[0].Detail.Es());
        Assert.All(explanation.Business.Skip(1), i => Assert.Equal(ExplanationOutcome.NotEvaluated, i.Outcome));
        Assert.Equal(["DriverAllocationService", "DriverEligibilityCheck", "DriverUnavailableException", "LegalValidationService", "LegalComplianceEvaluation",
            "VehicleEligibilityService", "ScheduleConflictService", "ScheduleConflictService", "Trip.AssignResources"], explanation.Flow.Select(s => s.Component));
        Assert.All(explanation.Flow.Skip(3), s => Assert.Equal(ExplanationOutcome.NotEvaluated, s.Outcome));
        Assert.Contains(DecisionExplainer.AdrDriverAvailable, explanation.Adrs);
        Assert.DoesNotContain(DecisionExplainer.AdrDriverAvailable, DecisionExplainer.ForAllocation(Trace(Compliance(2))).Adrs);
    }

    [Fact]
    public void ForAllocation_ComplianceFails_LaterStepsNotEvaluated()
    {
        // Trip of 7h → continuous (Art. 7) exceeded.
        var explanation = DecisionExplainer.ForAllocation(Trace(Compliance(7)));

        Assert.Equal(ExplanationOutcome.Failed, Assert.Single(explanation.Flow, s => s.Component == nameof(LegalComplianceEvaluation)).Outcome);
        Assert.Contains(explanation.Flow, s => s.Component == "ComplianceException");
        Assert.All(explanation.Flow.Where(s => s.Component is nameof(VehicleEligibilityService) or nameof(ScheduleConflictService)),
            s => Assert.Equal(ExplanationOutcome.NotEvaluated, s.Outcome));
        Assert.Contains(explanation.Business, i => i.Reference == "Art. 7" && i.Outcome == ExplanationOutcome.Failed && i.Detail.Es().Contains("7,0 h de 4,5 h"));
        Assert.Contains("rechaz", explanation.Summary.Es(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForAllocation_VehicleIneligible_ScheduleNotEvaluated()
    {
        var ineligible = VehicleEligibilityCheck.Ineligible([ComplianceViolation.Critical("Vehicle.Capacity", Texts.AsWritten("Not enough seats"))]);

        var explanation = DecisionExplainer.ForAllocation(Trace(Compliance(2), ineligible));

        Assert.Equal(ExplanationOutcome.Failed, Assert.Single(explanation.Flow, s => s.Component == nameof(VehicleEligibilityService)).Outcome);
        Assert.Contains(explanation.Flow, s => s.Component == "VehicleIneligibleException");
        Assert.All(explanation.Flow.Where(s => s.Component == nameof(ScheduleConflictService)),
            s => Assert.Equal(ExplanationOutcome.NotEvaluated, s.Outcome));
        Assert.Contains(explanation.Business, i => i.Outcome == ExplanationOutcome.Failed && i.Detail.Es().Contains("Not enough seats"));
    }

    [Fact]
    public void ForAllocation_DriverConflict_NamesConflictingTrips()
    {
        var explanation = DecisionExplainer.ForAllocation(Trace(
            Compliance(2), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.Conflict([42])));

        Assert.Contains(explanation.Business, i => i.Outcome == ExplanationOutcome.Failed && i.Detail.Es().Contains("#42"));
        Assert.Contains(explanation.Flow, s => s.Component == "ScheduleConflictException");
    }

    [Fact]
    public void PositionReason_ComparesWithPreviousCandidate()
    {
        Assert.Contains("primero", DecisionExplainer.PositionReason(1, null, CandidateProximity.Within5Km, 3.1).Es());
        Assert.Contains("banda mejor", DecisionExplainer.PositionReason(2, (CandidateProximity.BaseAtOrigin, 0), CandidateProximity.Within5Km, 3.1).Es());
        Assert.Contains("3,1 km frente a 1,2 km", DecisionExplainer.PositionReason(2, (CandidateProximity.Within5Km, 1.2), CandidateProximity.Within5Km, 3.1).Es());
        Assert.Contains("identificador", DecisionExplainer.PositionReason(3, (CandidateProximity.Within5Km, 3.1), CandidateProximity.Within5Km, 3.1).Es());
    }

    [Fact]
    public void ForDriverCandidates_IncludesBandRadiusComplianceAndAdr()
    {
        var driver = new Driver("ID7", "Ana", "Pérez", false, true);
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(driver, 7);
        var candidate = new DriverCandidate(driver, 1, CandidateProximity.Within10Km, 7.4, Texts.AsWritten("Base a 7.4 km"), [], Compliance(2));

        var explanation = Assert.Single(DecisionExplainer.ForDriverCandidates([candidate]));

        Assert.Contains(explanation.Business, i => i.Detail.Es().Contains("radio de 10 km") && i.Detail.Es().Contains("7,4 km"));
        Assert.Contains(explanation.Business, i => i.Reference == "Art. 6.1");
        Assert.Contains(explanation.Flow, s => s.Component == nameof(ProximityRankingPolicy));
        Assert.Contains(explanation.Adrs, a => a.Contains("0007"));
    }

    [Fact]
    public void ForDriverCandidates_SameBooking_StatesCoveredTripAsShortFact()
    {
        var driver = new Driver("ID8", "Luis", "Gil", false, true);
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(driver, 8);
        var candidate = new DriverCandidate(driver, 1, CandidateProximity.BaseAtOriginAndDestination, 0, Texts.AsWritten("Misma reserva: ya cubre el viaje #9"), [], Compliance(2), coversSameBooking: true)
        {
            SameBookingTripId = 9
        };

        var explanation = Assert.Single(DecisionExplainer.ForDriverCandidates([candidate]));

        // Facts the code checks, no narrative.
        Assert.Contains(explanation.Business, i => i.Title.Es() == "Misma reserva" && i.Detail.Es().StartsWith("Ya cubre el viaje #9 de esta reserva.", StringComparison.Ordinal));
        Assert.Contains(explanation.Business, i => i.Detail.Es() == "Su base es la salida y la llegada (mejor banda).");
        Assert.DoesNotContain(explanation.Business, i => i.Detail.Es().Contains("vuelve a su base"));
    }

    private static DriverCandidate Tied(int rank, double hours, bool won, IReadOnlyCollection<ComplianceViolation>? warnings = null) =>
        new(new Driver($"ID{rank}", "Ana", "Pérez", false, true), rank, CandidateProximity.Within5Km, 3.1, Texts.AsWritten("Base a 3.1 km"), warnings ?? [], Compliance(2))
        {
            HoursSinceWeeklyRest = TimeSpan.FromHours(hours),
            WonTieBreak = won
        };

    [Fact]
    public void ForDriverCandidates_TieDecidedByRestHours_SaysHoursNotIdentifier()
    {
        var explanations = DecisionExplainer.ForDriverCandidates([Tied(1, 6, won: true), Tied(2, 10, won: false)]);

        var position = Assert.Single(explanations[1].Business, i => i.Title.Es() == "Posición 2").Detail;
        Assert.Contains("10,0 h", position.Es());
        Assert.Contains("6,0 h", position.Es());
        Assert.DoesNotContain("identificador", position.Es());
    }

    [Fact]
    public void ForDriverCandidates_TieOnHoursToo_SaysIdentifier()
    {
        var explanations = DecisionExplainer.ForDriverCandidates([Tied(1, 6, won: false), Tied(2, 6, won: false)]);

        Assert.Contains("identificador", Assert.Single(explanations[1].Business, i => i.Title.Es() == "Posición 2").Detail.Es());
    }

    [Fact]
    public void ForDriverCandidates_WithWarnings_SummaryDoesNotClaimEveryRulePassed()
    {
        var warnings = LegalComplianceEvaluation.Validate([], Day.AddHours(11), TimeSpan.FromHours(2)).Violations.ToList();
        Assert.NotEmpty(warnings); // Weekly rest not verifiable without history (SPEC 0040).

        var explanation = Assert.Single(DecisionExplainer.ForDriverCandidates([Tied(1, 6, won: false, warnings: warnings)]));

        Assert.Contains($"{warnings.Count} aviso", explanation.Summary.Es());
        Assert.DoesNotContain("todos", explanation.Summary.Es());
    }

    [Fact]
    public void AdrReferences_AllExistInRepository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DispatchGuard.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        Assert.NotEmpty(DecisionExplainer.AllAdrs);
        Assert.All(DecisionExplainer.AllAdrs, adr => Assert.True(File.Exists(Path.Combine(root!.FullName, adr)), adr));
    }
}
