using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

public class VehicleLastServiceReader : IVehicleLastServiceReader
{
    private readonly DispatchGuardDbContext _db;

    public VehicleLastServiceReader(DispatchGuardDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyList<Trip>> LastServicesAsync(IReadOnlyCollection<int> vehicleIds, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        if (vehicleIds.Count == 0) return [];
        // One row per vehicle, whatever the size of the history: the newest departure (the highest id if two leave at once).
        return await _db.Vehicles.Where(v => vehicleIds.Contains(v.Id))
            .SelectMany(v => _db.Trips
                .Where(t => t.AssignedVehicleId == v.Id && t.AssignedDriverId != null && t.IsActive && t.Status != TripStatus.Cancelled
                    && t.ScheduledWindow.Start <= now)
                .OrderByDescending(t => t.ScheduledWindow.Start).ThenByDescending(t => t.Id)
                .Take(1))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
