using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>Reads always load the trips.</summary>
public interface IBookingRepository : IGenericRepository<Booking>
{
    /// <summary>
    /// Active bookings with at least one trip overlapping <c>[from, to)</c>, ordered by their first trip. Each comes with all its
    /// trips, also those outside the range, and their driver and vehicle.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetByRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
