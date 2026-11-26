using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

public interface IClientRepository : IGenericRepository<Client>
{
    Task<IEnumerable<Client>> GetActiveClientsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<Client>> GetExternalClientsAsync(CancellationToken cancellationToken);

    /// <summary>Inactive clients count: the index is unique over all of them. <paramref name="exceptId"/>: the one being changed.</summary>
    Task<bool> ExistsByTaxIdAsync(string taxId, int? exceptId = null, CancellationToken cancellationToken = default);
}