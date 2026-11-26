using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    ITripRepository Trips { get; }
    IBookingRepository Bookings { get; }
    IDriverRepository Drivers { get; }
    IVehicleRepository Vehicles { get; }
    ILocationRepository Locations { get; }
    ICircuitRepository Circuits { get; }
    IClientRepository Clients { get; }
    IBaseRepository Bases { get; }
    ICachedRouteRepository CachedRoutes { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction that holds <paramref name="claim"/> for itself in the database, so
    /// what it reads cannot change until it has saved (SPEC 0065, ADR 0018). <paramref name="work"/> must read everything it
    /// decides on (nothing read before is kept) and save; it may run more than once after a transient failure.
    /// </summary>
    /// <param name="claim">Services, drivers and vehicles it needs.</param>
    /// <param name="work">Reads, decides and saves.</param>
    /// <param name="written">An entity that <paramref name="work"/> wrote, to know if a commit that failed was in fact done.</param>
    /// <param name="cancellationToken">Cancels the wait and the work; nothing is saved.</param>
    /// <exception cref="DispatchGuard.Core.Exceptions.ResourceBusyException">The wait ran out. Nothing was saved.</exception>
    Task<T> ExecuteExclusiveAsync<T>(
        ResourceClaim claim,
        Func<CancellationToken, Task<T>> work,
        Func<T, Entity> written,
        CancellationToken cancellationToken = default);
}
