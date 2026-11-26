using System.Diagnostics;
using DispatchGuard.Core.Localization;

namespace DispatchGuard.Core.Auditing;

public sealed record TracedRule(string Id, int Passed, int Failed);

/// <summary>Every time a stage ran, added up: a recommendation evaluates hundreds of pairs.</summary>
public sealed record TracedStage(string Stage, int Times, double Milliseconds, int Queries);

public sealed record TracedLock(string Resource, int Count);

/// <summary>Written and waiting for its transaction, committed with it, or left behind by a transaction that never committed.</summary>
public enum WriteOutcome
{
    Pending = 0,
    Saved = 1,
    Discarded = 2
}

public sealed record TracedWrite(string Entity, int Count, WriteOutcome Outcome);

/// <param name="Actual">For a CE 561/2006 rule, the figure with the trip; <paramref name="Limit"/> comes from <c>DrivingLimits</c>.</param>
public sealed record TracedRuleFigure(string Rule, string? Article, bool Passed, double? Actual, double? Limit, string? Unit, LocalizedText? Name = null);

/// <param name="Outcome">One of <see cref="CheckOutcomes"/>.</param>
public sealed record TracedCheck(string Step, string Outcome, IReadOnlyList<TracedRuleFigure> Rules);

public static class CheckOutcomes
{
    public const string Passed = "passed";
    public const string Rejected = "rejected";

    /// <summary>A forced assignment went past it (SPEC 0056).</summary>
    public const string Overridden = "overridden";

    /// <summary>An earlier check already said no.</summary>
    public const string NotReached = "not-reached";
}

/// <summary>The stages an operation can mark, in the order an assignment goes through them.</summary>
public static class TraceStages
{
    public const string Locks = "locks";
    public const string Read = "read";
    public const string DriverAvailable = "driver-available";
    public const string Compliance = "compliance";
    public const string Vehicle = "vehicle";
    public const string DriverOverlap = "driver-overlap";
    public const string VehicleOverlap = "vehicle-overlap";
    public const string Assign = "assign";
    public const string Record = "record";
    public const string Commit = "commit";
}

/// <param name="Sql">The text as it is sent, with its parameters and not their values.</param>
public sealed record TracedQuery(string Sql, int Times, double Milliseconds);

/// <summary>
/// What the operation in progress evaluates and asks the database, if someone is collecting it (SPEC 0085 R6). Ambient, so that
/// the layers that know each thing do not have to carry it in their signatures.
/// </summary>
public sealed class OperationTrace
{
    /// <summary>The rest are still counted.</summary>
    private const int MaxDifferentQueries = 40;

    private static readonly AsyncLocal<OperationTrace?> Ambient = new();

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (int Passed, int Failed)> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Times, double Milliseconds)> _queries = new(StringComparer.Ordinal);
    private readonly List<TracedStage> _stages = [];
    private readonly List<TracedLock> _locks = [];
    private readonly List<TracedWrite> _writes = [];
    private (string Stage, long Since, int Queries)? _open;

    public static OperationTrace? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    public int QueryCount { get; private set; }

    public double QueryMilliseconds { get; private set; }

    public IReadOnlyList<TracedRule> Rules
    {
        get
        {
            lock (_lock) return [.. _rules.Select(rule => new TracedRule(rule.Key, rule.Value.Passed, rule.Value.Failed))];
        }
    }

    public IReadOnlyList<TracedQuery> Queries
    {
        get
        {
            lock (_lock) return [.. _queries.Select(query => new TracedQuery(query.Key, query.Value.Times, query.Value.Milliseconds))];
        }
    }

    /// <summary>The engine reached an assignment this many times; <see cref="FirstVerdict"/> is the first one.</summary>
    public int Evaluations { get; private set; }

    /// <summary>Each check of the first evaluation, in contract order (SPEC 0097).</summary>
    public IReadOnlyList<TracedCheck>? FirstVerdict { get; private set; }

    public IReadOnlyList<TracedStage> Stages
    {
        get
        {
            lock (_lock) return [.. _stages];
        }
    }

    public IReadOnlyList<TracedLock> Locks
    {
        get
        {
            lock (_lock) return [.. _locks];
        }
    }

    public IReadOnlyList<TracedWrite> Writes
    {
        get
        {
            lock (_lock) return [.. _writes];
        }
    }

    public void Decided(IReadOnlyList<TracedCheck> checks)
    {
        lock (_lock)
        {
            Evaluations++;
            FirstVerdict ??= checks;
        }
    }

    /// <summary>Ends the stage in progress, if any, and starts <paramref name="stage"/>: stages follow one another, never nest.</summary>
    public void Begin(string stage)
    {
        lock (_lock)
        {
            CloseStage();
            _open = (stage, Stopwatch.GetTimestamp(), QueryCount);
        }
    }

    public void End()
    {
        lock (_lock) CloseStage();
    }

    public void Locked(string resource, int count)
    {
        if (count == 0) return;
        lock (_lock) Add(_locks, l => l.Resource == resource, l => l with { Count = l.Count + count }, new TracedLock(resource, count));
    }

    /// <summary>Written in the current transaction; it counts as saved once it commits.</summary>
    public void Written(string entity, int count)
    {
        if (count == 0) return;
        lock (_lock) _writes.Add(new TracedWrite(entity, count, WriteOutcome.Pending));
    }

    public void Committed()
    {
        lock (_lock) Settle(WriteOutcome.Saved);
    }

    /// <summary>A new transaction starts: whatever the previous one left written and did not commit is gone.</summary>
    public void Discarded()
    {
        lock (_lock) Settle(WriteOutcome.Discarded);
    }

    private void Settle(WriteOutcome outcome)
    {
        for (var i = 0; i < _writes.Count; i++)
        {
            if (_writes[i].Outcome == WriteOutcome.Pending) _writes[i] = _writes[i] with { Outcome = outcome };
        }
    }

    private void CloseStage()
    {
        if (_open is not { } open) return;
        _open = null;
        var milliseconds = Stopwatch.GetElapsedTime(open.Since).TotalMilliseconds;
        var queries = QueryCount - open.Queries;
        Add(_stages, s => s.Stage == open.Stage,
            s => s with { Times = s.Times + 1, Milliseconds = s.Milliseconds + milliseconds, Queries = s.Queries + queries },
            new TracedStage(open.Stage, 1, milliseconds, queries));
    }

    private static void Add<T>(List<T> items, Predicate<T> same, Func<T, T> merge, T item)
    {
        var index = items.FindIndex(same);
        if (index < 0) items.Add(item);
        else items[index] = merge(items[index]);
    }

    public void Evaluated(string rule, bool passed)
    {
        lock (_lock)
        {
            var (passes, failures) = _rules.GetValueOrDefault(rule);
            _rules[rule] = passed ? (passes + 1, failures) : (passes, failures + 1);
        }
    }

    public void Queried(string sql, TimeSpan duration)
    {
        lock (_lock)
        {
            QueryCount++;
            QueryMilliseconds += duration.TotalMilliseconds;
            if (_queries.TryGetValue(sql, out var seen))
            {
                _queries[sql] = (seen.Times + 1, seen.Milliseconds + duration.TotalMilliseconds);
            }
            else if (_queries.Count < MaxDifferentQueries)
            {
                _queries[sql] = (1, duration.TotalMilliseconds);
            }
        }
    }
}
