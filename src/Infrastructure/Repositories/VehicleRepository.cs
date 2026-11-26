using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>Reads load the unavailabilities: availability and eligibility checks work on the in-memory collection.</summary>
public class VehicleRepository : EfRepository<Vehicle>, IVehicleRepository
{
    public VehicleRepository(DispatchGuardDbContext context) : base(context)
    {
    }

    protected override IQueryable<Vehicle> Query() => Set.Include(e => e.Unavailabilities);

    public Task<bool> ExistsByLicensePlateAsync(string licensePlate, int? exceptId = null, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(v => v.LicensePlate == licensePlate && (exceptId == null || v.Id != exceptId), cancellationToken);
}
