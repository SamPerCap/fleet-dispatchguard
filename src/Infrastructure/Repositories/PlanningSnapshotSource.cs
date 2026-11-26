using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>Without tracking: what is simulated on these reads can never reach the database (SPEC 0062).</summary>
public sealed class PlanningSnapshotSource : IPlanningSnapshotSource
{
    private readonly DispatchGuardDbContext _db;

    public PlanningSnapshotSource(DispatchGuardDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyList<Trip>> PendingAsync(DateTime now, int? bookingId, int? baseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var query = _db.Trips.AsNoTracking().Where(t => t.IsActive && !t.IsLocked
            && (t.Status == TripStatus.Pending || t.Status == TripStatus.ValidationFailed)
            && t.AssignedDriverId == null && t.AssignedVehicleId == null
            && t.ScheduledWindow.Start > now);
        if (bookingId is { } booking) query = query.Where(t => t.BookingId == booking);
        if (baseId is { } operatingBase) query = query.Where(t => t.BaseId == operatingBase);
        if (to is { } until) query = query.Where(t => t.ScheduledWindow.Start < until);
        if (from is { } since) query = query.Where(t => t.ScheduledWindow.End > since);
        return await query.OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Trip>> TripsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default) =>
        await _db.Trips.AsNoTracking().Where(t => tripIds.Contains(t.Id)).ToListAsync(cancellationToken);

    public async Task<PlanningSnapshot> LoadAsync(DateTime from, DateTime to, IReadOnlyCollection<int> bookingIds, CancellationToken cancellationToken = default)
    {
        var trips = await _db.Trips.AsNoTracking()
            .Where(t => (t.ScheduledWindow.Start < to && t.ScheduledWindow.End > from) || bookingIds.Contains(t.BookingId))
            .ToListAsync(cancellationToken);
        var drivers = await _db.Drivers.AsNoTracking().Include(d => d.Unavailabilities).ToListAsync(cancellationToken);
        var vehicles = await _db.Vehicles.AsNoTracking().Include(v => v.Unavailabilities).ToListAsync(cancellationToken);
        var locations = await _db.Locations.AsNoTracking().ToListAsync(cancellationToken);
        var customers = await _db.Bookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.CustomerName, cancellationToken);
        return new PlanningSnapshot(trips, drivers, vehicles, locations, customers);
    }

    public Task<bool> BookingExistsAsync(int bookingId, CancellationToken cancellationToken = default) =>
        _db.Bookings.AnyAsync(b => b.Id == bookingId, cancellationToken);

    public Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default) =>
        _db.Bases.AnyAsync(b => b.Id == baseId && b.IsActive, cancellationToken);
}
