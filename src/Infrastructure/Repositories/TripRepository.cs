using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>
/// Every query is paged with a stable order (<c>ScheduledWindow.Start</c>, then <c>Id</c>); date ranges are half-open
/// <c>[from, to)</c> and match trips whose window overlaps them.
/// </summary>
public class TripRepository : EfRepository<Trip>, ITripRepository
{
    public TripRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    protected override IQueryable<Trip> Query() => Set.Include(t => t.IntermediateStops);

    /// <summary>
    /// Through <see cref="Trip.Cancel"/>, because a deleted trip must stop counting as driving history and as a schedule conflict,
    /// and both filters work on the status.
    /// </summary>
    public override Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.Cancel();
        return UpdateAsync(entity, cancellationToken);
    }

    public Task<PagedResult<Trip>> GetByDriverAsync(int driverId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default)
    {
        var query = Overlapping(Query().Where(t => t.AssignedDriverId == driverId), from, to);
        return PageAsync(Excluding(query, excludeTripId), page, pageSize, cancellationToken);
    }

    public Task<PagedResult<Trip>> GetByVehicleAsync(int vehicleId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default)
    {
        var query = Overlapping(Query().Where(t => t.AssignedVehicleId == vehicleId), from, to);
        return PageAsync(Excluding(query, excludeTripId), page, pageSize, cancellationToken);
    }

    public Task<PagedResult<Trip>> GetByDateRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) =>
        PageAsync(Overlapping(Query(), from, to), page, pageSize, cancellationToken);

    public async Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
        await Query().Where(t => t.BookingId == bookingId)
            .OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default)
    {
        var windows = await Set.AsNoTracking()
            .Where(t => t.IsActive && t.Status != TripStatus.Cancelled
                && t.OriginLocationId == originLocationId && t.DestinationLocationId == destinationLocationId)
            .Select(t => new { t.ScheduledWindow.Start, t.ScheduledWindow.End })
            .ToListAsync(cancellationToken);
        return windows.Select(w => w.End - w.Start).ToList();
    }

    public async Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTripIds);
        return sourceTripIds.Count == 0 ? []
            : await Query().Where(t => t.ForcedByTrips.Any(f => sourceTripIds.Contains(f.SourceTripId)))
                .OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id)
                .ToListAsync(cancellationToken);
    }

    public Task<PagedResult<Trip>> GetByStatusAsync(TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        PageAsync(Query().Where(t => t.Status == status), page, pageSize, cancellationToken);

    /// <summary>With <c>[from, to)</c>: starts before <c>to</c> and ends after <c>from</c>.</summary>
    private static IQueryable<Trip> Overlapping(IQueryable<Trip> query, DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
        {
            throw new ArgumentException("to must not be before from", nameof(to));
        }

        if (to.HasValue)
        {
            query = query.Where(t => t.ScheduledWindow.Start < to.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(t => t.ScheduledWindow.End > from.Value);
        }

        return query;
    }

    private static IQueryable<Trip> Excluding(IQueryable<Trip> query, int? excludeTripId) =>
        excludeTripId.HasValue ? query.Where(t => t.Id != excludeTripId.Value) : query;

    private static async Task<PagedResult<Trip>> PageAsync(IQueryable<Trip> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var totalCount = await query.CountAsync(cancellationToken);

        // Callers ask for "everything" with pageSize = int.MaxValue: compute the offset in long.
        var skip = (long)(page - 1) * pageSize;
        if (skip >= totalCount)
        {
            return PagedResult<Trip>.Create([], totalCount, page, pageSize);
        }

        var items = await query
            .OrderBy(t => t.ScheduledWindow.Start)
            .ThenBy(t => t.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return PagedResult<Trip>.Create(items, totalCount, page, pageSize);
    }
}
