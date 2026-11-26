using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>Saves each attempt at once, in the transaction that is open if there is one.</summary>
public class AllocationAttemptLog : IAllocationAttemptLog
{
    private readonly DispatchGuardDbContext _context;
    private readonly ICurrentSession? _session;

    public AllocationAttemptLog(DispatchGuardDbContext context, ICurrentSession? session = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _session = session;
    }

    public async Task RecordAsync(AllocationAttempt attempt, CancellationToken cancellationToken = default)
    {
        attempt.MadeBy(_session?.Id);
        _context.AllocationAttempts.Add(attempt);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _context.Entry(attempt).State = EntityState.Detached;
            throw;
        }
    }
}
