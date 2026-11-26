using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Interfaces.Repositories;

namespace DispatchGuard.Application.Services.Engine;

/// <summary>Register, KPIs and hourly curve come from the same filter, so that the figures match the rows (SPEC 0059).</summary>
public interface IEngineAuditService
{
    Task<EngineAuditPageDto> AttemptsAsync(EngineAuditQuery query, int page, int pageSize, CancellationToken ct = default);
    Task<EngineAttemptDetailDto> AttemptAsync(int id, CancellationToken ct = default);
    Task<EngineAuditHourlyDto> HourlyAsync(EngineAuditQuery query, CancellationToken ct = default);
}

public sealed class EngineAuditService : IEngineAuditService
{
    public const int MaxPageSize = 100;
    /// <summary>
    /// The page shows one operational day or the week that ends on it; the resource card asks for the 28 days of its history
    /// (<see cref="Resources.ResourceHistory.Days"/>).
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedDays = [1, 7, Resources.ResourceHistory.Days];

    private static readonly IReadOnlySet<string> CatalogIds =
        RulesCatalog.Build().SelectMany(g => g.Rules).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

    private readonly IAllocationAttemptAudit _audit;

    public EngineAuditService(IAllocationAttemptAudit audit) => _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    /// <summary>[From, To) in UTC: the operational days (Europe/Madrid) ending with the one containing <paramref name="date"/>.</summary>
    public static (DateTime From, DateTime To) Period(DateTime date, int days)
    {
        if (!AllowedDays.Contains(days)) throw new InvalidRequestException(Texts.FieldOneOf(nameof(days), string.Join(", ", AllowedDays)));
        var (from, to) = OperationalDay.Containing(date);
        for (var i = 1; i < days; i++) from = OperationalDay.Before(from).From;
        return (from, to);
    }

    public async Task<EngineAuditPageDto> AttemptsAsync(EngineAuditQuery query, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) throw new InvalidRequestException(Texts.FieldAtLeast(nameof(page), 1));
        if (pageSize < 1 || pageSize > MaxPageSize) throw new InvalidRequestException(Texts.FieldBetween(nameof(pageSize), 1, MaxPageSize));
        var filter = await FilterAsync(query, ct);

        var result = await _audit.SearchAsync(filter with { Outcomes = ControlCenterService.OutcomesOf(query.Kind) }, page, pageSize, ct);
        var labels = await _audit.TripLabelsAsync(result.Page.Items.Select(a => a.TripId).Distinct().ToList(), ct);
        var items = result.Page.Items.Select(a => Row(a, labels)).ToList();
        return new EngineAuditPageDto(query.BaseId, filter.From, filter.To, EngineCountsDto.From(result.CountsByOutcome),
            items, result.Page.Page, result.Page.PageSize, result.Page.TotalCount, result.Page.TotalPages, result.People, AttemptRules.Audited);
    }

    public async Task<EngineAttemptDetailDto> AttemptAsync(int id, CancellationToken ct = default)
    {
        var attempt = await _audit.GetAsync(id, ct) ?? throw MissingResource.AllocationAttempt(id);
        var labels = await _audit.TripLabelsAsync([attempt.TripId], ct);
        return new EngineAttemptDetailDto(Row(attempt, labels), AttemptTexts.Of(attempt).Explanation);
    }

    public async Task<EngineAuditHourlyDto> HourlyAsync(EngineAuditQuery query, CancellationToken ct = default)
    {
        // The three series come from the same rows, so the result filter does not apply here (SPEC 0059 R2).
        var filter = await FilterAsync(query, ct);
        var attempts = await _audit.ListAsync(filter, ct);
        return new EngineAuditHourlyDto(query.BaseId, filter.From, filter.To, ControlTrends.Hourly(attempts, filter.From, filter.To));
    }

    private async Task<AttemptAuditFilter> FilterAsync(EngineAuditQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(query.Kind)) throw new InvalidRequestException(Texts.UnknownValue(nameof(query.Kind), query.Kind.ToString()));
        if (query.Origin is { } origin && !Enum.IsDefined(origin)) throw new InvalidRequestException(Texts.UnknownValue(nameof(query.Origin), origin.ToString()));
        var rule = string.IsNullOrWhiteSpace(query.Rule) ? null : query.Rule.Trim();
        if (rule is not null && !CatalogIds.Contains(rule)) throw new InvalidRequestException(Texts.UnknownValue(nameof(query.Rule), rule));
        if (query.BaseId is { } baseId && !await _audit.BaseExistsAsync(baseId, ct)) throw MissingResource.Base(baseId);
        var (from, to) = Period(query.Date, query.Days);
        var by = string.IsNullOrWhiteSpace(query.By) ? null : query.By.Trim();
        if (by is not null && by.Contains('\0')) throw new InvalidRequestException(Texts.NullCharacter()); // PostgreSQL does not admit it.
        EntityValidator.ValidateMaxLength(by, AllocationAttempt.AttemptedByMaxLength, Texts.NameCannot());
        return new AttemptAuditFilter(from, to, query.BaseId, null, rule, query.DriverId, query.VehicleId, by, query.Origin);
    }

    private static EngineAttemptDto Row(AllocationAttempt attempt, IReadOnlyDictionary<int, string?> labels) =>
        new(attempt.Id, attempt.AttemptedAt, attempt.AttemptedBy, attempt.BaseId, attempt.TripId, labels.TryGetValue(attempt.TripId, out var label) ? label : null,
            attempt.DriverId, attempt.VehicleId, attempt.Outcome, AttemptTexts.Of(attempt).Detail, attempt.RuleIds, attempt.ForcedReason, attempt.Explanation is not null, attempt.Origin, attempt.ShortSessionId);
}
