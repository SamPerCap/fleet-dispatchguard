using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DispatchGuard.Infrastructure.Demo;

/// <summary>
/// Hard delete in foreign-key order inside one transaction; the distance cache (<c>CachedRoutes</c>) is kept,
/// and so is what is recorded of the visits (SPEC 0085 R8).
/// Idempotent: on an empty database it deletes nothing.
/// </summary>
public sealed class DemoDataReset(DispatchGuardDbContext db, ITripDurationCache? tripDurations = null) : IDemoDataReset
{
    private readonly DispatchGuardDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    private const string AuditedEntity = "Demo";

    /// <summary>First half of its advisory lock; those of the resources are in <see cref="AdvisoryResourceLocks"/>.</summary>
    private const int LockKind = 0x4C4F4604;

    /// <summary>
    /// A connection of its own whose transaction stays open and empty: behind a connection pooler only a lock of the transaction
    /// lasts (ADR 0024).
    /// </summary>
    public async Task<IAsyncDisposable?> TryReserveAsync(CancellationToken ct = default)
    {
        // It retries: it may be the first thing asked to a database that is asleep.
        var connection = await _db.Database.CreateExecutionStrategy().ExecuteAsync(OpenAsync, ct);
        var isReserved = false;
        try
        {
            var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock($1, 0)", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = LockKind });
            isReserved = await command.ExecuteScalarAsync(ct) is true;
            return isReserved ? connection : null;
        }
        finally
        {
            if (!isReserved)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        // The retrying execution strategy only allows a user transaction inside its own Execute.
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.AllocationAttempts.ExecuteDeleteAsync(ct);
            await _db.TripStops.ExecuteDeleteAsync(ct);
            await _db.Trips.ExecuteDeleteAsync(ct);
            await _db.Circuits.ExecuteDeleteAsync(ct);
            await _db.DriverUnavailabilities.ExecuteDeleteAsync(ct);
            await _db.VehicleUnavailabilities.ExecuteDeleteAsync(ct);
            await _db.Drivers.ExecuteDeleteAsync(ct);
            await _db.Vehicles.ExecuteDeleteAsync(ct);
            await _db.Clients.ExecuteDeleteAsync(ct);
            await _db.Bases.Where(b => b.Id != DispatchGuard.Core.Entities.OperatingBase.DefaultId).ExecuteDeleteAsync(ct); // The default base is kept.
            await _db.Locations.ExecuteDeleteAsync(ct);
            await _db.AuditAsync(AuditActions.Reset, AuditedEntity, ct);
            await tx.CommitAsync(ct);
        });
        tripDurations?.Invalidate(); // After the commit: trips and location ids are gone.
    }
}
