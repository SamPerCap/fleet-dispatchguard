using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;

namespace DispatchGuard.Infrastructure.Repositories;

public class CachedRouteRepository : EfRepository<CachedRoute>, ICachedRouteRepository
{
    public CachedRouteRepository(DispatchGuardDbContext context) : base(context)
    {
    }
}
