using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Resources;

public sealed record AffectedTripDto(
    int TripId, int BookingId, string CustomerName, DateTime Start, DateTime End, string Status,
    int OriginLocationId, int DestinationLocationId, int? AssignedDriverId, int? AssignedVehicleId, int RequiredSeats);

/// <summary>
/// The assigned services an absence of [from, to) would catch (SPEC 0078 R4), whatever its length. It only informs: an absence
/// takes nothing away from anyone (ADR 0019).
/// </summary>
public interface IAffectedTripsQuery
{
    Task<IReadOnlyList<AffectedTripDto>> DriverAsync(int driverId, DateTime from, DateTime to, CancellationToken ct = default);

    Task<IReadOnlyList<AffectedTripDto>> VehicleAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default);
}

public sealed class AffectedTripsQuery : IAffectedTripsQuery
{
    private readonly IUnitOfWork _uow;
    private readonly IAssignedTripReader _trips;

    public AffectedTripsQuery(IUnitOfWork uow, IAssignedTripReader trips)
    {
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _trips = trips ?? throw new ArgumentNullException(nameof(trips));
    }

    public async Task<IReadOnlyList<AffectedTripDto>> DriverAsync(int driverId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var period = AgendaEntryRules.Period(from, to);
        _ = await _uow.Drivers.GetByIdAsync(driverId, ct) ?? throw MissingResource.Driver(driverId);
        return Caught(await _trips.OfDriverAsync(driverId, period, ct), period);
    }

    public async Task<IReadOnlyList<AffectedTripDto>> VehicleAsync(int vehicleId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var period = AgendaEntryRules.Period(from, to);
        _ = await _uow.Vehicles.GetByIdAsync(vehicleId, ct) ?? throw MissingResource.Vehicle(vehicleId);
        return Caught(await _trips.OfVehicleAsync(vehicleId, period, ct), period);
    }

    private static IReadOnlyList<AffectedTripDto> Caught(IReadOnlyList<AssignedTrip> trips, TimeWindow period) =>
        trips.Where(t => period.OverlapsWith(t.Window))
            .Select(t => new AffectedTripDto(
                t.TripId, t.BookingId, t.CustomerName, t.Window.Start, t.Window.End, t.Status.ToString(),
                t.OriginLocationId, t.DestinationLocationId, t.DriverId, t.VehicleId, t.RequiredSeats))
            .ToList();
}
