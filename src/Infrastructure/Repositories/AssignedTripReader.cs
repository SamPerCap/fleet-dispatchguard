using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

public class AssignedTripReader : IAssignedTripReader
{
    private readonly DispatchGuardDbContext _db;

    public AssignedTripReader(DispatchGuardDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public Task<IReadOnlyList<AssignedTrip>> OfDriverAsync(int driverId, TimeWindow period, CancellationToken cancellationToken = default) =>
        ReadAsync(_db.Trips.Where(t => t.AssignedDriverId == driverId), period, cancellationToken);

    public Task<IReadOnlyList<AssignedTrip>> OfVehicleAsync(int vehicleId, TimeWindow period, CancellationToken cancellationToken = default) =>
        ReadAsync(_db.Trips.Where(t => t.AssignedVehicleId == vehicleId), period, cancellationToken);

    private static async Task<IReadOnlyList<AssignedTrip>> ReadAsync(IQueryable<Trip> trips, TimeWindow period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        var rows = await trips.AsNoTracking()
            .Where(t => t.IsActive && t.Status != TripStatus.Cancelled
                && t.ScheduledWindow.Start < period.End && t.ScheduledWindow.End > period.Start)
            .OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id, t.BookingId, t.Booking.CustomerName, t.ScheduledWindow.Start, t.ScheduledWindow.End, t.Status,
                t.OriginLocationId, t.DestinationLocationId, t.AssignedDriverId, t.AssignedVehicleId, t.RequiredSeats
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new AssignedTrip(
            r.Id, r.BookingId, r.CustomerName, new TimeWindow(r.Start, r.End), r.Status,
            r.OriginLocationId, r.DestinationLocationId, r.AssignedDriverId, r.AssignedVehicleId, r.RequiredSeats)).ToList();
    }
}
