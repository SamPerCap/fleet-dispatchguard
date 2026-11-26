using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>
/// [<see cref="From"/>, <see cref="To"/>) in UTC. A null filter means everything; the rule is a catalog id and the person an
/// exact name.
/// </summary>
public sealed record AttemptAuditFilter(
    DateTime From, DateTime To, int? BaseId = null, IReadOnlyCollection<AllocationOutcome>? Outcomes = null,
    string? RuleId = null, int? DriverId = null, int? VehicleId = null, string? AttemptedBy = null,
    AttemptOrigin? Origin = null);

/// <summary>A page of the register plus what the KPIs and the filters need, all from one filter.</summary>
/// <param name="CountsByOutcome">With every filter except <see cref="AttemptAuditFilter.Outcomes"/>.</param>
/// <param name="People">Who attempted something in the period and base, whatever the other filters say; by name.</param>
public sealed record AttemptAuditPage(
    PagedResult<AllocationAttempt> Page, IReadOnlyDictionary<AllocationOutcome, int> CountsByOutcome, IReadOnlyList<string> People);

public interface IAllocationAttemptAudit
{
    /// <summary>Newest first; <paramref name="page"/> is 1-based.</summary>
    Task<AttemptAuditPage> SearchAsync(AttemptAuditFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<AllocationAttempt>> ListAsync(AttemptAuditFilter filter, CancellationToken cancellationToken = default);

    Task<AllocationAttempt?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default);

    /// <summary>Description of each trip, null when it has none.</summary>
    Task<IReadOnlyDictionary<int, string?>> TripLabelsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default);
}
