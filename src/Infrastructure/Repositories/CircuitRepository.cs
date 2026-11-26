using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;

namespace DispatchGuard.Infrastructure.Repositories;

public class CircuitRepository : EfRepository<Circuit>, ICircuitRepository
{
    public CircuitRepository(DispatchGuardDbContext context) : base(context)
    {
    }
}
