using DispatchGuard.Benchmarks;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Tests.Benchmarks;

/// <summary>
/// The benchmark scenarios must exercise the path they claim to measure, otherwise the published figures would describe
/// something else (SPEC 0039 R2).
/// </summary>
public class ComplianceScenariosTests
{
    [Fact]
    public void NewDriver_NoHistory_CompliantWithWarningOnly()
    {
        var scenario = ComplianceScenarios.NewDriver();

        var result = LegalComplianceEvaluation.Validate(scenario.History, scenario.TripStart, scenario.TripDuration);

        Assert.Empty(scenario.History);
        Assert.True(result.IsCompliant);
        Assert.NotEmpty(result.Violations);
        Assert.All(result.Violations, v => Assert.Equal(ViolationSeverity.Warning, v.Severity));
    }

    [Fact]
    public void RegularTwoWeeks_80HoursIn14Days_CompliantWithoutViolations()
    {
        var scenario = ComplianceScenarios.RegularTwoWeeks();

        var result = LegalComplianceEvaluation.Validate(scenario.History, scenario.TripStart, scenario.TripDuration);

        Assert.Equal(20, scenario.History.Count);
        Assert.True(result.IsCompliant, string.Join(" | ", result.Violations.Select(v => v.RuleId + ": " + v.Message)));
        Assert.Equal(TimeSpan.FromHours(80), TimeSpan.FromTicks(scenario.History.Sum(p => p.Duration.Ticks)));
        Assert.True(result.IsCompliant);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void ContinuousDrivingExceeded_TripRightAfter4hBlock_NonCompliant()
    {
        var scenario = ComplianceScenarios.ContinuousDrivingExceeded();

        var result = LegalComplianceEvaluation.Validate(scenario.History, scenario.TripStart, scenario.TripDuration);

        Assert.False(result.IsCompliant);
        Assert.Contains(result.Violations, v => v.Severity == ViolationSeverity.Critical);
    }
}
