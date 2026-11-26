namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>
/// What an operation needs for itself, by id (SPEC 0065). The drivers and vehicles that the services have assigned when they are
/// taken are added on their own; the order in which everything is taken is not the caller's choice.
/// </summary>
public sealed record ResourceClaim
{
    public IReadOnlyCollection<int> TripIds { get; private init; } = [];

    public IReadOnlyCollection<int> DriverIds { get; private init; } = [];

    public IReadOnlyCollection<int> VehicleIds { get; private init; } = [];

    private ResourceClaim()
    {
    }

    public static ResourceClaim Allocation(int tripId, int driverId, int vehicleId) =>
        new() { TripIds = [tripId], DriverIds = [driverId], VehicleIds = [vehicleId] };

    public static ResourceClaim Trips(IReadOnlyCollection<int> tripIds) => new() { TripIds = tripIds };

    public static ResourceClaim Driver(int driverId) => new() { DriverIds = [driverId] };

    public static ResourceClaim Vehicle(int vehicleId) => new() { VehicleIds = [vehicleId] };
}
