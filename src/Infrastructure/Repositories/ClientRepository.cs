using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

public class ClientRepository : EfRepository<Client>, IClientRepository
{
    public ClientRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    /// <summary><see cref="EfRepository{T}.GetAllAsync"/> already skips the inactive ones; this only names the use case.</summary>
    public Task<IEnumerable<Client>> GetActiveClientsAsync(CancellationToken cancellationToken) =>
        GetAllAsync(cancellationToken);

    public async Task<IEnumerable<Client>> GetExternalClientsAsync(CancellationToken cancellationToken) =>
        await Query().Where(c => c.IsExternal && c.IsActive).ToListAsync(cancellationToken);

    public Task<bool> ExistsByTaxIdAsync(string taxId, int? exceptId = null, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(c => c.TaxId == taxId && (exceptId == null || c.Id != exceptId), cancellationToken);
}
