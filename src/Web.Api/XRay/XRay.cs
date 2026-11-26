using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Localization;

namespace DispatchGuard.API.XRay;

/// <summary>
/// What a visitor may see of the answer they just asked for (SPEC 0097 R2): codes and numbers built field by field, never an internal
/// object. No SQL, no ids of locks or conflicts, no names, no reason of a forced assignment.
/// </summary>
public sealed record XRayDto(
    string Kind,
    int Status,
    double TotalMs,
    int Queries,
    double QueriesMs,
    int Evaluations,
    IReadOnlyList<XRayCheckDto> Checks,
    IReadOnlyList<XRayRuleCountDto> Rules,
    IReadOnlyList<XRayStageDto> Stages,
    double OtherMs,
    IReadOnlyList<XRayLockDto> Locks,
    IReadOnlyList<XRayWriteDto> Writes);

public sealed record XRayCheckDto(string Step, string Outcome, IReadOnlyList<XRayFigureDto> Rules);

public sealed record XRayFigureDto(string Rule, string? Name, string? Article, bool Passed, double? Actual, double? Limit, string? Unit);

public sealed record XRayRuleCountDto(string Rule, int Passed, int Failed);

public sealed record XRayStageDto(string Stage, int Times, double Ms, int Queries);

public sealed record XRayLockDto(string Resource, int Count);

public sealed record XRayWriteDto(string Record, int Count, bool Saved);

public static class XRayReport
{
    private const int MaxRules = 32;

    /// <summary>The only records shown: what an assignment saves. The rest, the audit included, is not the visitor's business.</summary>
    private static readonly Dictionary<string, string> ShownRecords = new(StringComparer.Ordinal)
    {
        ["Trip"] = "service",
        ["AllocationAttempt"] = "attempt"
    };

    private static readonly HashSet<string> LockKinds = new(StringComparer.Ordinal) { "trip", "driver", "vehicle" };

    public static XRayDto Of(string kind, int status, TimeSpan total, OperationTrace operation, Language language)
    {
        operation.End(); // A failure may have left a stage open: it ends with the answer.
        var stages = operation.Stages.Select(s => new XRayStageDto(s.Stage, s.Times, Rounded(s.Milliseconds), s.Queries)).ToList();
        var totalMs = Rounded(total.TotalMilliseconds);
        return new XRayDto(
            kind,
            status,
            totalMs,
            operation.QueryCount,
            Rounded(operation.QueryMilliseconds),
            operation.Evaluations,
            ChecksOf(operation, language),
            [.. operation.Rules.Take(MaxRules).Select(r => new XRayRuleCountDto(r.Id, r.Passed, r.Failed))],
            stages,
            Math.Max(0, Rounded(totalMs - stages.Sum(s => s.Ms))),
            [.. operation.Locks.Where(l => LockKinds.Contains(l.Resource)).Select(l => new XRayLockDto(l.Resource, l.Count))],
            WritesOf(operation));
    }

    /// <summary>The order of the checks only means something when there was one evaluation.</summary>
    private static IReadOnlyList<XRayCheckDto> ChecksOf(OperationTrace operation, Language language) =>
        operation is { Evaluations: 1, FirstVerdict: { } checks }
            ? [.. checks.Select(check => new XRayCheckDto(check.Step, check.Outcome, [.. check.Rules.Take(MaxRules).Select(f => FigureOf(f, language))]))]
            : [];

    private static XRayFigureDto FigureOf(TracedRuleFigure figure, Language language) =>
        new(figure.Rule, figure.Name?.In(language), figure.Article, figure.Passed, Figure(figure.Actual), Figure(figure.Limit), figure.Unit);

    private static List<XRayWriteDto> WritesOf(OperationTrace operation) =>
    [
        .. operation.Writes
            .Where(w => ShownRecords.ContainsKey(w.Entity))
            .GroupBy(w => (Record: ShownRecords[w.Entity], Saved: w.Outcome == WriteOutcome.Saved))
            .Select(g => new XRayWriteDto(g.Key.Record, g.Sum(w => w.Count), g.Key.Saved))
    ];

    private static double Rounded(double milliseconds) => Math.Round(milliseconds, 1);

    private static double? Figure(double? value) => value is { } number ? Math.Round(number, 2) : null;
}
