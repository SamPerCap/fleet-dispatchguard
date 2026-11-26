using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface IVehicleRepository : IGenericRepository<Vehicle>
{
    /// <summary>Inactive vehicles count: the index is unique over all of them. <paramref name="exceptId"/>: the one being changed.</summary>
    Task<bool> ExistsByLicensePlateAsync(string licensePlate, int? exceptId = null, CancellationToken cancellationToken = default);
}
