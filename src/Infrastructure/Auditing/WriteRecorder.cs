using System.Data.Common;
using DispatchGuard.Core.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DispatchGuard.Infrastructure.Auditing;

/// <summary>
/// Adds to the <see cref="OperationTrace"/> in progress which entities a save writes and whether their transaction committed (SPEC 0097).
/// Only counts: it never reads a value nor changes what is saved.
/// </summary>
internal sealed class WriteRecorder : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (OperationTrace.Current is { } operation && eventData.Context is { } context)
        {
            var written = context.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .GroupBy(entry => entry.Metadata.ClrType.Name);
            foreach (var entity in written)
            {
                operation.Written(entity.Key, entity.Count());
            }
        }

        return ValueTask.FromResult(result);
    }

    /// <summary>Without a transaction of its own, the save is already in the database.</summary>
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null) OperationTrace.Current?.Committed();
        return ValueTask.FromResult(result);
    }
}

/// <summary>A transaction disposed without committing may not say so: what it left pending is reported as not saved anyway.</summary>
internal sealed class TransactionRecorder : DbTransactionInterceptor
{
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OperationTrace.Current?.Committed();
        return Task.CompletedTask;
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        OperationTrace.Current?.Discarded();
        return Task.CompletedTask;
    }
}
