using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface IDriverRepository : IGenericRepository<Driver>
{
    /// <summary>Inactive drivers count: the index is unique over all of them. <paramref name="exceptId"/>: the one being changed.</summary>
    Task<bool> ExistsByIdentificationNumberAsync(string identificationNumber, int? exceptId = null, CancellationToken cancellationToken = default);
}