using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>Where each vehicle really is, by its last service done or running (SPEC 0071 R2).</summary>
public interface IVehicleLastServiceReader
{
    /// <summary>
    /// Departed at <paramref name="now"/> (<see cref="Trip.HasDeparted"/>), active, not cancelled and with driver and vehicle,
    /// whatever its base and however long ago. Vehicles without one are left out.
    /// </summary>
    Task<IReadOnlyList<Trip>> LastServicesAsync(IReadOnlyCollection<int> vehicleIds, DateTime now, CancellationToken cancellationToken = default);
}
