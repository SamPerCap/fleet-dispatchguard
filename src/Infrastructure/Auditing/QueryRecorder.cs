using System.Data.Common;
using DispatchGuard.Core.Auditing;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DispatchGuard.Infrastructure.Auditing;

/// <summary>
/// Adds each command to the <see cref="OperationTrace"/> in progress. The text has parameters and not values: those are never read.
/// </summary>
internal sealed class QueryRecorder : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command, eventData);
        return Task.CompletedTask;
    }

    private static void Record(DbCommand command, CommandEndEventData eventData) =>
        OperationTrace.Current?.Queried(command.CommandText, eventData.Duration);
}
