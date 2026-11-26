using DispatchGuard.Core.Auditing;

namespace Core.Tests.Auditing;

/// <summary>The marks the X-ray reads (SPEC 0097): stages follow one another, and a write only counts as saved once it commits.</summary>
public class OperationTraceTests
{
    [Fact]
    public void Begin_ANewStage_EndsThePreviousOneAndCountsItsQueries()
    {
        var operation = new OperationTrace();

        operation.Begin(TraceStages.Locks);
        operation.Queried("SELECT 1", TimeSpan.FromMilliseconds(1));
        operation.Begin(TraceStages.Read);
        operation.Queried("SELECT 2", TimeSpan.FromMilliseconds(1));
        operation.Queried("SELECT 3", TimeSpan.FromMilliseconds(1));
        operation.End();
        operation.Queried("SELECT 4", TimeSpan.FromMilliseconds(1));

        Assert.Equal([(TraceStages.Locks, 1, 1), (TraceStages.Read, 1, 2)], operation.Stages.Select(s => (s.Stage, s.Times, s.Queries)));
        Assert.Equal(4, operation.QueryCount);
    }

    [Fact]
    public void Begin_TheSameStageAgain_AddsItUpInItsFirstPlace()
    {
        var operation = new OperationTrace();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            operation.Begin(TraceStages.DriverAvailable);
            operation.Begin(TraceStages.Compliance);
            operation.End();
        }

        Assert.Equal([(TraceStages.DriverAvailable, 3), (TraceStages.Compliance, 3)], operation.Stages.Select(s => (s.Stage, s.Times)));
    }

    [Fact]
    public void End_WithoutAStage_DoesNothing()
    {
        var operation = new OperationTrace();

        operation.End();

        Assert.Empty(operation.Stages);
    }

    [Fact]
    public void Written_IsSavedOnlyByItsCommit_AndWhatWasLeftBeforeIsDiscarded()
    {
        var operation = new OperationTrace();

        operation.Written("Trip", 1);
        operation.Discarded();
        operation.Written("Trip", 1);
        operation.Written("AllocationAttempt", 1);
        operation.Committed();
        operation.Written("AllocationAttempt", 1);

        Assert.Equal(
            [("Trip", WriteOutcome.Discarded), ("Trip", WriteOutcome.Saved), ("AllocationAttempt", WriteOutcome.Saved), ("AllocationAttempt", WriteOutcome.Pending)],
            operation.Writes.Select(w => (w.Entity, w.Outcome)));
    }

    [Fact]
    public void Locked_AddsUpByKind_AndNothingIsNothing()
    {
        var operation = new OperationTrace();

        operation.Locked("driver", 1);
        operation.Locked("vehicle", 0);
        operation.Locked("driver", 1);

        Assert.Equal([("driver", 2)], operation.Locks.Select(l => (l.Resource, l.Count)));
    }

    [Fact]
    public void Decided_KeepsTheFirstVerdictAndCountsEvery()
    {
        var operation = new OperationTrace();
        IReadOnlyList<TracedCheck> first = [new(TraceStages.DriverAvailable, CheckOutcomes.Rejected, [])];

        operation.Decided(first);
        operation.Decided([new(TraceStages.DriverAvailable, CheckOutcomes.Passed, [])]);

        Assert.Equal(2, operation.Evaluations);
        Assert.Same(first, operation.FirstVerdict);
    }
}
