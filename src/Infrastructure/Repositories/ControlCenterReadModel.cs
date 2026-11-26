using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>No tracking: nothing here is saved.</summary>
public class ControlCenterReadModel : IControlCenterReadModel
{
    private readonly DispatchGuardDbContext _db;

    public ControlCenterReadModel(DispatchGuardDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyList<OperatingBase>> BasesAsync(CancellationToken cancellationToken = default) =>
        await _db.Bases.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(cancellationToken);

    public Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default) =>
        _db.Bases.AnyAsync(b => b.Id == baseId && b.IsActive, cancellationToken);

    public async Task<IReadOnlyList<Trip>> TripsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        await _db.Trips.AsNoTracking()
            .Where(t => (baseId == null || t.BaseId == baseId) && t.IsActive && t.Status != TripStatus.Cancelled
                && t.ScheduledWindow.Start < to && t.ScheduledWindow.End > from)
            .OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Driver>> DriversAsync(int? baseId, CancellationToken cancellationToken = default) =>
        await _db.Drivers.AsNoTracking().Include(d => d.Unavailabilities)
            .Where(d => baseId == null || d.BaseId == baseId).OrderBy(d => d.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> VehiclesAsync(int? baseId, CancellationToken cancellationToken = default) =>
        await _db.Vehicles.AsNoTracking().Include(v => v.Unavailabilities)
            .Where(v => baseId == null || v.BaseId == baseId).OrderBy(v => v.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Booking>> BookingsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        await _db.Bookings.AsNoTracking().Include(b => b.Trips).AsSplitQuery()
            .Where(b => b.IsActive && b.Trips.Any(t => (baseId == null || t.BaseId == baseId) && t.IsActive
                && t.ScheduledWindow.Start < to && t.ScheduledWindow.End > from))
            .OrderBy(b => b.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AllocationAttempt>> AttemptsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        await _db.AllocationAttempts.AsNoTracking()
            .Where(a => (baseId == null || a.BaseId == baseId) && a.AttemptedAt >= from && a.AttemptedAt < to)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AllocationAttempt>> ActivityAsync(int? baseId, DateTime since, IReadOnlyCollection<AllocationOutcome>? outcomes,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _db.AllocationAttempts.AsNoTracking().Where(a => (baseId == null || a.BaseId == baseId) && a.AttemptedAt >= since);
        if (outcomes is not null) query = query.Where(a => outcomes.Contains(a.Outcome));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(a => a.AttemptedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return PagedResult<AllocationAttempt>.Create(items, total, page, pageSize);
    }

    public async Task<IReadOnlyDictionary<int, string?>> TripLabelsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default)
    {
        if (tripIds.Count == 0) return new Dictionary<int, string?>();
        return await _db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Description, cancellationToken);
    }
}
