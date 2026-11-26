using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>A null <c>baseId</c> means every base (SPEC 0057 R1).</summary>
public interface IControlCenterReadModel
{
    /// <summary>Active ones, by name.</summary>
    Task<IReadOnlyList<OperatingBase>> BasesAsync(CancellationToken cancellationToken = default);

    Task<bool> BaseExistsAsync(int baseId, CancellationToken cancellationToken = default);

    /// <summary>Active and not cancelled, overlapping [from, to).</summary>
    Task<IReadOnlyList<Trip>> TripsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>Inactive ones included, with their unavailabilities.</summary>
    Task<IReadOnlyList<Driver>> DriversAsync(int? baseId, CancellationToken cancellationToken = default);

    /// <summary>Inactive ones included, with their unavailabilities.</summary>
    Task<IReadOnlyList<Vehicle>> VehiclesAsync(int? baseId, CancellationToken cancellationToken = default);

    /// <summary>Active bookings with a trip of the base in [from, to), each with all its trips.</summary>
    Task<IReadOnlyList<Booking>> BookingsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>In [from, to).</summary>
    Task<IReadOnlyList<AllocationAttempt>> AttemptsAsync(int? baseId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>Newest first; null <paramref name="outcomes"/> means all of them.</summary>
    Task<PagedResult<AllocationAttempt>> ActivityAsync(int? baseId, DateTime since, IReadOnlyCollection<AllocationOutcome>? outcomes,
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Description of each trip, null when it has none.</summary>
    Task<IReadOnlyDictionary<int, string?>> TripLabelsAsync(IReadOnlyCollection<int> tripIds, CancellationToken cancellationToken = default);
}
