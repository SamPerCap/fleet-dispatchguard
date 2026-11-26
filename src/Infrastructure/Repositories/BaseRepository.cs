using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

public class BaseRepository : EfRepository<OperatingBase>, IBaseRepository
{
    public BaseRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    public Task<bool> ExistsByNameAsync(string name, int? exceptId = null, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(b => b.Name == name && (exceptId == null || b.Id != exceptId), cancellationToken);
}
