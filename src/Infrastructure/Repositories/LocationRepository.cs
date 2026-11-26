using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;

namespace DispatchGuard.Infrastructure.Repositories;

public class LocationRepository : EfRepository<Location>, ILocationRepository
{
    public LocationRepository(DispatchGuardDbContext context) : base(context)
    {
    }
}
