using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Engine;

public class EngineAuditTests
{
    private static readonly DateTime Day = new(2026, 11, 16);
    private static readonly HashSet<string> CatalogIds = RulesCatalog.Build().SelectMany(g => g.Rules).Select(r => r.Id).ToHashSet();

    private static ComplianceCheckResult Legal() =>
        LegalComplianceEvaluation.Validate([new DrivingPeriod(1, Day.AddHours(6), Day.AddHours(8))], Day.AddHours(11), TimeSpan.FromHours(2));

    private static readonly TimeWindow Window = new(Day.AddHours(11), Day.AddHours(13));

    private static DriverEligibilityCheck Available => DriverEligibilityCheck.For(new Driver("1", "Ana", "Gil"), Window);

    private static DriverEligibilityCheck OnLeave(bool active = true)
    {
        var driver = new Driver("1", "Ana", "Gil");
        driver.AddUnavailability(new DriverUnavailability(1, Window, UnavailabilityType.SickLeave));
        if (!active) driver.Deactivate();
        return DriverEligibilityCheck.For(driver, Window);
    }

    private static AllocationTrace Trace(ComplianceCheckResult compliance, VehicleEligibilityCheck? eligibility = null,
        ScheduleConflictCheck? driver = null, ScheduleConflictCheck? vehicle = null) =>
        new(Available, compliance, eligibility, driver, vehicle, RequiredSeats: 40, SeatingCapacity: 55);

    [Fact]
    public void Of_AllPassed_ReturnsNoRules()
    {
        var trace = Trace(Legal(), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.NoConflict());

        Assert.Empty(AttemptRules.Of(trace));
    }

    [Fact]
    public void Of_DailyDrivingExceeded_ReturnsCatalogIdOfTheBrokenRule()
    {
        // 06:00–10:00 + 11:00–18:00 = 11 h > 10 h (even an extended day).
        var compliance = LegalComplianceEvaluation.Validate(
            [new DrivingPeriod(1, Day.AddHours(6), Day.AddHours(10))], Day.AddHours(11), TimeSpan.FromHours(7));
        Assert.False(compliance.IsCompliant);

        var rules = AttemptRules.Of(Trace(compliance));

        Assert.Contains("daily", rules);
        Assert.All(rules, r => Assert.Contains(r, CatalogIds));
    }

    [Theory]
    [InlineData("CE_561_2006.ContinuousDriving.4_5hours", "continuous")]
    [InlineData("CE_561_2006.DailyDriving.9hours", "daily")]
    [InlineData("CE_561_2006.WeeklyDriving.56hours", "weekly")]
    [InlineData("CE_561_2006.BiWeeklyDriving.90hours", "biweekly")]
    [InlineData("CE_561_2006.DailyRest.11hours", "daily-rest")]
    [InlineData("CE_561_2006.DailyRest.ReducedLimit", "daily-rest-reduced")]
    [InlineData("CE_561_2006.WeeklyRest.45hours", "weekly-rest")]
    [InlineData("DriverEligibility.NotActive", "driver-active")]
    [InlineData("DriverEligibility.Unavailable.SickLeave", "driver-unavailable")]
    [InlineData("DriverEligibility.Unavailable.Vacation", "driver-unavailable")]
    [InlineData("VehicleEligibility.NotActive", "vehicle-active")]
    [InlineData("VehicleEligibility.InsufficientCapacity", "vehicle-seats")]
    [InlineData("VehicleEligibility.Unavailable.Maintenance", "vehicle-unavailable")]
    public void CatalogId_MapsEveryEngineRuleToAnExistingCatalogRule(string engineId, string catalogId)
    {
        Assert.Equal(catalogId, AttemptRules.CatalogId(engineId));
        Assert.Contains(catalogId, CatalogIds);
    }

    [Fact]
    public void Audited_AreCatalogBlockingRules_IncludingOverlaps()
    {
        var blocking = RulesCatalog.Build().SelectMany(g => g.Rules).Where(r => r.Kind == RuleKind.Blocking).Select(r => r.Id).ToHashSet();
        Assert.All(AttemptRules.Audited, id => Assert.Contains(id, blocking));
        Assert.Contains(AttemptRules.DriverOverlap, AttemptRules.Audited);
        Assert.Contains(AttemptRules.VehicleOverlap, AttemptRules.Audited);
        Assert.Equal(AttemptRules.Audited.Count, AttemptRules.Audited.Distinct().Count());
    }

    [Fact]
    public void Of_LaterServiceBroken_ReturnsTheRuleAndTheArticleItBreaks()
    {
        // 10:00–12:00 right before a 4 h service: 6 h of continuous driving for the later one (SPEC 0063).
        List<DrivingPeriod> calendar = [new(1, Day.AddHours(12), Day.AddHours(16), 20)];
        var compliance = LegalComplianceEvaluation.Validate(calendar, Day.AddHours(10), TimeSpan.FromHours(2))
            .WithLaterServices(LaterServicesEvaluation.Evaluate(calendar, new DrivingPeriod(1, Day.AddHours(10), Day.AddHours(12), 10)));

        Assert.Equal(["later-services", "continuous"], AttemptRules.Of(Trace(compliance)));
        Assert.Contains(AttemptRules.LaterServices, AttemptRules.Audited);
        Assert.Contains(AttemptRules.LaterServices, CatalogIds);
    }

    [Fact]
    public void CatalogId_UnknownEngineRule_ReturnsNull() => Assert.Null(AttemptRules.CatalogId("Something.Else"));

    [Fact]
    public void Of_VehicleAndSchedule_ReturnTheirCatalogIds()
    {
        var vehicle = VehicleEligibilityCheck.Ineligible([ComplianceViolation.Critical("VehicleEligibility.InsufficientCapacity", Texts.AsWritten("x"))]);
        Assert.Equal(["vehicle-seats"], AttemptRules.Of(Trace(Legal(), vehicle)));

        var driverClash = Trace(Legal(), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.Conflict([7]));
        Assert.Equal(["driver-overlap"], AttemptRules.Of(driverClash));

        var vehicleClash = Trace(Legal(), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.Conflict([8]));
        Assert.Equal(["vehicle-overlap"], AttemptRules.Of(vehicleClash));
    }

    [Fact]
    public void Of_DriverUnavailable_ReturnsItsRules_WithoutCompliance()
    {
        Assert.Equal(["driver-unavailable"], AttemptRules.Of(new AllocationTrace(OnLeave(), null, null, null, null, 40, 55)));
        Assert.Equal(["driver-active", "driver-unavailable"], AttemptRules.Of(new AllocationTrace(OnLeave(active: false), null, null, null, null, 40, 55)));
    }

    [Fact]
    public void Of_WarningOnly_IsNotABrokenRule()
    {
        var warned = ComplianceCheckResult.CompliantWithWarnings(
            [ComplianceViolation.Warning("CE_561_2006.WeeklyRest.Unverifiable", Texts.AsWritten("x"))], TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Empty(AttemptRules.Of(Trace(warned, VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.NoConflict())));
    }

    [Fact]
    public void Counts_ValidationsAreAcceptedPlusBlockedPlusForced()
    {
        var counts = EngineCountsDto.From(new Dictionary<AllocationOutcome, int>
        {
            [AllocationOutcome.Assigned] = 5,
            [AllocationOutcome.RejectedCompliance] = 3,
            [AllocationOutcome.RejectedVehicle] = 2,
            [AllocationOutcome.RejectedSchedule] = 1,
            [AllocationOutcome.RejectedDriver] = 7,
            [AllocationOutcome.Forced] = 4,
        });

        Assert.Equal(5, counts.Accepted);
        Assert.Equal(13, counts.Blocked);
        Assert.Equal(4, counts.Forced);
        Assert.Equal(22, counts.Validations);
        Assert.Equal(counts.Accepted + counts.Blocked + counts.Forced, counts.Validations);
    }

    [Fact]
    public void Counts_Empty_AreAllZero()
    {
        var counts = EngineCountsDto.From(new Dictionary<AllocationOutcome, int>());

        Assert.Equal(new EngineCountsDto(0, 0, 0, 0), counts);
    }

    [Fact]
    public void Counts_EveryOutcomeFallsInExactlyOneKpi()
    {
        foreach (var outcome in Enum.GetValues<AllocationOutcome>())
        {
            var counts = EngineCountsDto.From(new Dictionary<AllocationOutcome, int> { [outcome] = 1 });
            Assert.Equal(1, counts.Accepted + counts.Blocked + counts.Forced);
            Assert.Equal(1, counts.Validations);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Period_IsTheOperationalDaysEndingOnTheDate(int days)
    {
        var date = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

        var (from, to) = EngineAuditService.Period(date, days);

        Assert.Equal(new DateTime(2026, 11, 18, 23, 0, 0, DateTimeKind.Utc), to); // 00:00 of the 19th in Madrid (UTC+1)
        Assert.Equal(to.AddDays(-days), from);
    }

    [Fact]
    public void Period_28Days_IsTheResourceHistoryWindow_AcrossTheDstChange()
    {
        // 22 Oct–18 Nov; 22 Oct starts at 00:00 CEST (UTC+2) = 21 Oct 22:00Z.
        var date = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

        var (from, to) = EngineAuditService.Period(date, 28);

        Assert.Equal(new DateTime(2026, 11, 18, 23, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(new DateTime(2026, 10, 21, 22, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(DispatchGuard.Application.Services.Resources.ResourceHistory.Window(date)[0].From, from);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(27)]
    [InlineData(29)]
    public void Period_OnlyOneSevenOrTwentyEightDays(int days) =>
        Assert.ThrowsAny<ArgumentException>(() => EngineAuditService.Period(DateTime.UtcNow, days));

    [Fact]
    public void ExplanationJson_RoundTripsTheExplanation()
    {
        var explanation = DecisionExplainer.ForAllocation(Trace(Legal(), VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.NoConflict()));

        var back = AttemptTexts.Of(Attempt(AttemptTexts.Serialize(explanation, explanation.Summary))).Explanation;

        Assert.NotNull(back);
        Assert.Equal(explanation.Summary, back!.Summary);
        Assert.Equal(explanation.Business, back.Business);
        Assert.Equal(explanation.Flow, back.Flow);
        Assert.Equal(explanation.Patterns, back.Patterns);
        Assert.Equal(explanation.Adrs, back.Adrs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    public void ExplanationJson_MissingOrBroken_IsNull(string? json) =>
        Assert.Null(AttemptTexts.Of(Attempt(json)).Explanation);

    private static AllocationAttempt Attempt(string? explanation) =>
        new(DateTime.UtcNow, "Ana", 1, 1, 1, 1, AllocationOutcome.RejectedCompliance, "Detalle guardado", explanation: explanation);
}
