using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>A service assigned to a driver or a vehicle, with its customer, to show it.</summary>
public sealed record AssignedTrip(
    int TripId, int BookingId, string CustomerName, TimeWindow Window, TripStatus Status,
    int OriginLocationId, int DestinationLocationId, int? DriverId, int? VehicleId, int RequiredSeats);

/// <summary>
/// Not cancelled, around a period and by departure. It only narrows what is read: whether one overlaps is decided by
/// <see cref="TimeWindow.OverlapsWith"/>.
/// </summary>
public interface IAssignedTripReader
{
    Task<IReadOnlyList<AssignedTrip>> OfDriverAsync(int driverId, TimeWindow period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignedTrip>> OfVehicleAsync(int vehicleId, TimeWindow period, CancellationToken cancellationToken = default);
}
