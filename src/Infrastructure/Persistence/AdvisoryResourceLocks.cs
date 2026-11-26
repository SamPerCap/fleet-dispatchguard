using System.Diagnostics;
using System.Globalization;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>
/// Exclusive PostgreSQL advisory locks owned by the current transaction (SPEC 0065, ADR 0018): they work across API instances and
/// through a connection pooler, and go away with the transaction. Always taken services, drivers, vehicles and by id, so no two
/// operations can wait for each other.
/// </summary>
internal sealed class AdvisoryResourceLocks(DispatchGuardDbContext context, ResourceLockSettings settings)
{
    /// <summary>The server ends the wait by itself; the command only has to outlive it.</summary>
    private const int CommandMarginSeconds = 30;

    /// <summary>The server keeps the end of the wait, so that the whole batch is measured with one clock.</summary>
    private const string SetDeadline =
        "SELECT set_config('lof.lock_deadline', (clock_timestamp() + $1 * interval '1 millisecond')::text, true)";

    /// <summary>At least one millisecond: zero would mean waiting forever.</summary>
    private const string SetWaitLeft = """
        SELECT set_config('lock_timeout',
            greatest(1, ceil(extract(epoch FROM current_setting('lof.lock_deadline')::timestamptz - clock_timestamp()) * 1000))::bigint::text,
            true)
        """;

    private const string TakeLock = "SELECT pg_advisory_xact_lock($1, $2)";

    /// <summary>The limit is for these locks, not for the rows that the operation touches afterwards.</summary>
    private const string RestoreWait = "SET LOCAL lock_timeout TO DEFAULT";

    /// <summary>First half of the key of a lock; the second one is the whole id, so two resources never share a lock.</summary>
    private enum ResourceKind
    {
        Trip = 0x4C4F4601,
        Driver = 0x4C4F4602,
        Vehicle = 0x4C4F4603
    }

    private readonly record struct Resource(ResourceKind Kind, int Id)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"lof:{Kind.ToString().ToLowerInvariant()}:{Id}");
    }

    /// <summary>Takes <paramref name="claim"/> plus the drivers and vehicles its services have now.</summary>
    /// <exception cref="ResourceBusyException">The wait ran out.</exception>
    public async Task AcquireAsync(ResourceClaim claim, CancellationToken cancellationToken)
    {
        OperationTrace.Current?.Begin(TraceStages.Locks);
        var clock = Stopwatch.StartNew();
        var (drivers, vehicles) = (claim.DriverIds.ToHashSet(), claim.VehicleIds.ToHashSet());

        if (claim.TripIds.Count > 0)
        {
            await TakeAsync(InOrder(ResourceKind.Trip, claim.TripIds), clock, cancellationToken);

            // With the services taken nobody can change their resources.
            var current = await context.Trips.AsNoTracking()
                .Where(t => claim.TripIds.Contains(t.Id))
                .Select(t => new { t.AssignedDriverId, t.AssignedVehicleId })
                .ToListAsync(cancellationToken);
            drivers.UnionWith(current.Select(t => t.AssignedDriverId).OfType<int>());
            vehicles.UnionWith(current.Select(t => t.AssignedVehicleId).OfType<int>());
        }

        await TakeAsync(
            [.. InOrder(ResourceKind.Driver, drivers), .. InOrder(ResourceKind.Vehicle, vehicles)], clock, cancellationToken);
    }

    private static List<Resource> InOrder(ResourceKind kind, IEnumerable<int> ids) =>
        [.. ids.Distinct().Order().Select(id => new Resource(kind, id))];

    /// <summary>One round trip: takes <paramref name="resources"/> in the given order with what is left of the wait.</summary>
    private async Task TakeAsync(IReadOnlyList<Resource> resources, Stopwatch clock, CancellationToken cancellationToken)
    {
        if (resources.Count == 0)
        {
            return;
        }

        var left = (int)Math.Max(0, settings.LockWaitMilliseconds - clock.ElapsedMilliseconds);
        await using var batch = ((NpgsqlConnection)context.Database.GetDbConnection()).CreateBatch();
        batch.Transaction = (NpgsqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction();
        batch.Timeout = (left / 1_000) + CommandMarginSeconds;
        batch.BatchCommands.Add(Command(SetDeadline, left));
        foreach (var resource in resources)
        {
            // Apart from the lock: the limit of a wait is the one there was when its statement started.
            batch.BatchCommands.Add(Command(SetWaitLeft));
            batch.BatchCommands.Add(Command(TakeLock, (int)resource.Kind, resource.Id));
        }

        batch.BatchCommands.Add(Command(RestoreWait));

        var answered = 0;
        try
        {
            await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
            do
            {
                answered++;
            }
            while (await reader.NextResultAsync(cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // One answer for the deadline and two for each resource before the one that did not come.
            throw new ResourceBusyException(resources[(answered - 2) / 2].ToString());
        }

        if (OperationTrace.Current is { } operation)
        {
            // By kind and how many, never which: the previous driver of a service is someone else's data (SPEC 0097 R2).
            foreach (var taken in resources.GroupBy(r => r.Kind))
            {
                operation.Locked(taken.Key.ToString().ToLowerInvariant(), taken.Count());
            }
        }
    }

    private static NpgsqlBatchCommand Command(string sql, params int[] values)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = value });
        }

        return command;
    }
}
