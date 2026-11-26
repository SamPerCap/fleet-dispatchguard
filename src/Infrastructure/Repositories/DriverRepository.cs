using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>Reads load the unavailabilities: availability and eligibility checks work on the in-memory collection.</summary>
public class DriverRepository : EfRepository<Driver>, IDriverRepository
{
    public DriverRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    protected override IQueryable<Driver> Query() => Set.Include(e => e.Unavailabilities);

    public Task<bool> ExistsByIdentificationNumberAsync(string identificationNumber, int? exceptId = null, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(d => d.IdentificationNumber == identificationNumber && (exceptId == null || d.Id != exceptId), cancellationToken);
}
