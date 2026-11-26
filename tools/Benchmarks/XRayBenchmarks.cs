using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using DispatchGuard.API.XRay;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Localization;
using Microsoft.AspNetCore.DataProtection;

namespace DispatchGuard.Benchmarks;

/// <summary>
/// What the X-ray adds to one allocation (SPEC 0097 CA4): the marks the engine leaves as it goes, sealing the X-ray into the answer,
/// and opening it when the visitor asks. The algorithms of the ephemeral provider are the defaults, the same as with the session key.
/// </summary>
[MemoryDiagnoser]
public class XRayBenchmarks
{
    private const string Session = "0123456789abcdef0123456789abcdef";

    private static readonly string[] Steps =
    [
        TraceStages.Locks, TraceStages.Read, TraceStages.DriverAvailable, TraceStages.Compliance, TraceStages.Vehicle,
        TraceStages.DriverOverlap, TraceStages.VehicleOverlap, TraceStages.Assign, TraceStages.Record, TraceStages.Commit
    ];

    private readonly XRayTokens _tokens = new(new EphemeralDataProtectionProvider(), TimeProvider.System);
    private OperationTrace _done = null!;
    private string _sealed = null!;

    [GlobalSetup]
    public void Setup()
    {
        _done = MarkedAllocation();
        _sealed = Seal();
    }

    [Benchmark]
    [SuppressMessage("Performance", "CA1822", Justification = "BenchmarkDotNet only runs instance methods")]
    public OperationTrace Marks() => MarkedAllocation();

    [Benchmark]
    public string Seal() =>
        _tokens.Issue(Session, XRayReport.Of(XRayKinds.Allocation, 200, TimeSpan.FromMilliseconds(40), _done, Language.English));

    [Benchmark]
    public XRayDto? Open() => _tokens.Read(_sealed, Session);

    /// <summary>The marks of an accepted allocation with eight CE 561/2006 rules.</summary>
    private static OperationTrace MarkedAllocation()
    {
        var operation = new OperationTrace();
        foreach (var step in Steps)
        {
            operation.Begin(step);
            operation.Queried("SELECT 1", TimeSpan.FromMilliseconds(1));
        }

        operation.End();
        operation.Locked("trip", 1);
        operation.Locked("driver", 1);
        operation.Locked("vehicle", 1);
        operation.Written("Trip", 1);
        operation.Written("AllocationAttempt", 1);
        operation.Committed();
        var rules = Enumerable.Range(1, 8)
            .Select(i => new TracedRuleFigure($"CE_561_2006.Rule{i}", $"Art. {i}", true, 4.5, 9, "hours"))
            .ToList();
        operation.Decided(
        [
            new TracedCheck(TraceStages.DriverAvailable, CheckOutcomes.Passed, []),
            new TracedCheck(TraceStages.Compliance, CheckOutcomes.Passed, rules),
            new TracedCheck(TraceStages.Vehicle, CheckOutcomes.Passed, []),
            new TracedCheck(TraceStages.DriverOverlap, CheckOutcomes.Passed, []),
            new TracedCheck(TraceStages.VehicleOverlap, CheckOutcomes.Passed, [])
        ]);
        return operation;
    }
}
