using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>Copy of what the rules look at, detached from the database: nothing done to it can be saved (SPEC 0062, ADR 0020).</summary>
/// <param name="Trips">Trips overlapping the period, plus every trip of the bookings asked for.</param>
/// <param name="Drivers">Inactive ones included, with their unavailabilities.</param>
/// <param name="Vehicles">Inactive ones included, with their unavailabilities.</param>
public sealed record PlanningSnapshot(
    IReadOnlyList<Trip> Trips, IReadOnlyList<Driver> Drivers, IReadOnlyList<Vehicle> Vehicles, IReadOnlyList<Location> Locations,
    IReadOnlyDictionary<int, string> CustomerByBooking);

/// <summary>Never tracks what it returns.</summary>
public interface IPlanningSnapshotSource
{
    /// <summary>
    /// Trips that can be recommended: active, not locked, not started, without driver nor vehicle and departing after
    /// <paramref name="now"/>, of a booking, a base and/or overlapping [from, to). By departure.
    /// </summary>
    Task<IReadOnlyList<Trip>> PendingAsync(DateTime now, int? bookingId, int? baseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    /// <summary>Whatever their state.</summary>
    Task<IReadOnlyList<Trip>> TripsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default);

    Task<PlanningSnapshot> LoadAsync(DateTime from, DateTime to, IReadOnlyCollection<int> bookingIds, CancellationToken cancellationToken = default);

    Task<bool> BookingExistsAsync(int bookingId, CancellationToken cancellationToken = default);

    Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default);
}
