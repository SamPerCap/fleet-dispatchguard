using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>
/// The rule filter queries the array column of <see cref="AllocationAttempt.RuleIds"/>, so that paging and counts stay in SQL.
/// </summary>
public class AllocationAttemptAudit : IAllocationAttemptAudit
{
    private readonly DispatchGuardDbContext _db;

    public AllocationAttemptAudit(DispatchGuardDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<AttemptAuditPage> SearchAsync(AttemptAuditFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var scoped = Where(filter with { Outcomes = null });

        // The KPIs use the same filter without the result one (SPEC 0059 R1).
        var counts = await scoped.GroupBy(a => a.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var rows = filter.Outcomes is null ? scoped : scoped.Where(a => filter.Outcomes.Contains(a.Outcome));
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        // Everyone in the period and base, whatever the other filters say.
        var people = await Where(new AttemptAuditFilter(filter.From, filter.To, filter.BaseId))
            .Select(a => a.AttemptedBy).Distinct().OrderBy(n => n).ToListAsync(cancellationToken);

        return new AttemptAuditPage(PagedResult<AllocationAttempt>.Create(items, total, page, pageSize),
            counts.ToDictionary(c => c.Key, c => c.Count), people);
    }

    public async Task<IReadOnlyList<AllocationAttempt>> ListAsync(AttemptAuditFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return await Where(filter).OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id).ToListAsync(cancellationToken);
    }

    public Task<AllocationAttempt?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _db.AllocationAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default) =>
        _db.Bases.AnyAsync(b => b.Id == baseId && b.IsActive, cancellationToken);

    public async Task<IReadOnlyDictionary<int, string?>> TripLabelsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default)
    {
        if (tripIds.Count == 0) return new Dictionary<int, string?>();
        return await _db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Description, cancellationToken);
    }

    private IQueryable<AllocationAttempt> Where(AttemptAuditFilter filter)
    {
        var attempts = _db.AllocationAttempts.AsNoTracking().Where(a => a.AttemptedAt >= filter.From && a.AttemptedAt < filter.To);
        if (filter.BaseId is { } baseId) attempts = attempts.Where(a => a.BaseId == baseId);
        if (filter.Outcomes is { } outcomes) attempts = attempts.Where(a => outcomes.Contains(a.Outcome));
        if (filter.RuleId is { } rule) attempts = attempts.Where(a => a.RuleIds.Contains(rule));
        if (filter.DriverId is { } driverId) attempts = attempts.Where(a => a.DriverId == driverId);
        if (filter.VehicleId is { } vehicleId) attempts = attempts.Where(a => a.VehicleId == vehicleId);
        if (filter.AttemptedBy is { } by) attempts = attempts.Where(a => a.AttemptedBy == by);
        if (filter.Origin is { } origin) attempts = attempts.Where(a => a.Origin == origin);
        return attempts;
    }
}
