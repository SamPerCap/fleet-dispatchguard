using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>Only overlaps: CE 561/2006 and vehicle eligibility have their own services.</summary>
public interface IScheduleConflictService
{
    /// <summary>Cancelled trips never conflict.</summary>
    Task<ScheduleConflictCheck> CheckDriverConflict(int driverId, TimeWindow window, int? excludeTripId = null, CancellationToken cancellationToken = default);

    /// <summary>Cancelled trips never conflict.</summary>
    Task<ScheduleConflictCheck> CheckVehicleConflict(int vehicleId, TimeWindow window, int? excludeTripId = null, CancellationToken cancellationToken = default);
}

public sealed class ScheduleConflictService : IScheduleConflictService
{
    private readonly ITripRepository _tripRepository;

    public ScheduleConflictService(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository ?? throw new ArgumentNullException(nameof(tripRepository));
    }

    public async Task<ScheduleConflictCheck> CheckDriverConflict(int driverId, TimeWindow window, int? excludeTripId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var paged = await _tripRepository.GetByDriverAsync(driverId, window.Start, window.End, page: 1, pageSize: int.MaxValue, excludeTripId, cancellationToken);

        var conflicting = paged.Items
            .Where(t => t.Status != TripStatus.Cancelled)
            .Where(t => t.ScheduledWindow.OverlapsWith(window))
            .Select(t => t.Id)
            .ToList();

        return conflicting.Count == 0 ? ScheduleConflictCheck.NoConflict() : ScheduleConflictCheck.Conflict(conflicting);
    }

    public async Task<ScheduleConflictCheck> CheckVehicleConflict(int vehicleId, TimeWindow window, int? excludeTripId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        var paged = await _tripRepository.GetByVehicleAsync(vehicleId, window.Start, window.End, page: 1, pageSize: int.MaxValue, excludeTripId, cancellationToken);

        var conflicting = paged.Items
            .Where(t => t.Status != TripStatus.Cancelled)
            .Where(t => t.ScheduledWindow.OverlapsWith(window))
            .Select(t => t.Id)
            .ToList();

        return conflicting.Count == 0 ? ScheduleConflictCheck.NoConflict() : ScheduleConflictCheck.Conflict(conflicting);
    }
}
