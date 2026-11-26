using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Engine;

/// <summary>
/// <c>Validations = Accepted + Blocked + Forced</c> by construction: every outcome falls in exactly one of the three, as in
/// <c>engine-metrics</c> (SPEC 0057 R6).
/// </summary>
public sealed record EngineCountsDto(int Validations, int Accepted, int Blocked, int Forced)
{
    public static EngineCountsDto From(IReadOnlyDictionary<AllocationOutcome, int> byOutcome)
    {
        ArgumentNullException.ThrowIfNull(byOutcome);
        int Of(AllocationOutcome outcome) => byOutcome.TryGetValue(outcome, out var count) ? count : 0;
        var accepted = Of(AllocationOutcome.Assigned);
        var forced = Of(AllocationOutcome.Forced);
        var blocked = byOutcome.Where(kv => kv.Key is not (AllocationOutcome.Assigned or AllocationOutcome.Forced)).Sum(kv => kv.Value);
        return new EngineCountsDto(accepted + blocked + forced, accepted, blocked, forced);
    }
}

/// <summary><c>RuleIds</c> are catalog ids (<c>/api/rules</c>). <c>Who</c> is declared; <c>Session</c>, the start of the session that made it.</summary>
public sealed record EngineAttemptDto(
    int Id, DateTime At, string Who, int BaseId, int TripId, string? TripLabel, int DriverId, int VehicleId,
    AllocationOutcome Outcome, LocalizedText? Detail, IReadOnlyList<string> RuleIds, string? ForcedReason, bool HasExplanation,
    AttemptOrigin Origin = AttemptOrigin.Manual, string? Session = null);

/// <summary>The explanation is the one the dispatcher saw when it was decided; null for attempts older than SPEC 0059.</summary>
public sealed record EngineAttemptDetailDto(EngineAttemptDto Attempt, DecisionExplanation? Explanation);

/// <summary>
/// <c>Counts</c> apply every filter except the result one, so each KPI equals <c>TotalCount</c> when the register is filtered by
/// that result. <c>People</c>: who attempted in the period and base. <c>Rules</c>: the catalog ids an attempt can carry.
/// </summary>
public sealed record EngineAuditPageDto(
    int? BaseId, DateTime From, DateTime To, EngineCountsDto Counts,
    IReadOnlyList<EngineAttemptDto> Items, int Page, int PageSize, int TotalCount, int TotalPages, IReadOnlyList<string> People,
    IReadOnlyList<string> Rules);

/// <summary>With the register's filters except the result (SPEC 0059 R2).</summary>
public sealed record EngineAuditHourlyDto(int? BaseId, DateTime From, DateTime To, IReadOnlyList<Control.EngineHourDto> Hours);

/// <summary><c>Days</c>: 1 (the day), 7 (the week ending on it) or 28 (the history of a resource).</summary>
public sealed record EngineAuditQuery(
    DateTime Date, int Days = 1, int? BaseId = null, Control.ActivityKind Kind = Control.ActivityKind.All, string? Rule = null,
    int? DriverId = null, int? VehicleId = null, string? By = null,
    AttemptOrigin? Origin = null);
