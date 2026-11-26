using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>Only for CE 561/2006: rebuilds the continuous, daily and weekly driving of a driver.</summary>
public interface IDrivingRecordRepository
{
    /// <summary>Whole periods that overlap <paramref name="startDate"/>–<paramref name="endDate"/>, in chronological order.</summary>
    Task<IReadOnlyList<DrivingPeriod>> GetRecordsAsync(
        int driverId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);
}
