using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface IBaseRepository : IGenericRepository<OperatingBase>
{
    /// <summary>Inactive bases count: the index is unique over all of them. <paramref name="exceptId"/>: the one being changed.</summary>
    Task<bool> ExistsByNameAsync(string name, int? exceptId = null, CancellationToken cancellationToken = default);
}
