using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.Interfaces.Repositories;

/// <summary>It throws if it cannot save the record, and then there is no attempt (SPEC 0085 R4).</summary>
public interface IAllocationAttemptLog
{
    Task RecordAsync(AllocationAttempt attempt, CancellationToken cancellationToken = default);
}
