using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>The driving history of CE 561/2006 (continuous, daily and weekly) is rebuilt from the driver's assigned trips.</summary>
public class DrivingRecordRepository : IDrivingRecordRepository
{
    private readonly DispatchGuardDbContext _context;

    public DrivingRecordRepository(DispatchGuardDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<DrivingPeriod>> GetRecordsAsync(
        int driverId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("endDate must not be before startDate", nameof(endDate));
        }

        var trips = await _context.Trips
            .AsNoTracking()
            .Where(t => t.AssignedDriverId == driverId
                && TripStatusRules.Driving.Contains(t.Status)
                && t.ScheduledWindow.Start < endDate
                && t.ScheduledWindow.End > startDate)
            .OrderBy(t => t.ScheduledWindow.Start)
            .ToListAsync(cancellationToken);

        return trips
            .Select(t => new DrivingPeriod(driverId, t.ScheduledWindow.Start, t.ScheduledWindow.End, t.Id))
            .ToList();
    }
}
