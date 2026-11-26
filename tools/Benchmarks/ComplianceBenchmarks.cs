using BenchmarkDotNet.Attributes;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Benchmarks;

/// <summary>One evaluation is the check run for every candidate before proposing or allocating a driver (SPEC 0039).</summary>
[MemoryDiagnoser]
public class ComplianceBenchmarks
{
    [ParamsSource(nameof(Scenarios))]
    public ComplianceScenario Scenario { get; set; } = null!;

    public static IEnumerable<ComplianceScenario> Scenarios() => ComplianceScenarios.All();

    [Benchmark]
    public ComplianceCheckResult Validate() =>
        LegalComplianceEvaluation.Validate(Scenario.History, Scenario.TripStart, Scenario.TripDuration);
}
