using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface ITripRepository : IGenericRepository<Trip>
{
    Task<PagedResult<Trip>> GetByDriverAsync(int driverId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<Trip>> GetByVehicleAsync(int vehicleId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default);

    Task<PagedResult<Trip>> GetByDateRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Any status, ordered by start.</summary>
    Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default);

    /// <summary>Of active, non-cancelled trips.</summary>
    Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default);

    /// <summary>Trips marked as forced by any of the given services (SPEC 0063 R8).</summary>
    Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default);

    Task<PagedResult<Trip>> GetByStatusAsync(TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
}