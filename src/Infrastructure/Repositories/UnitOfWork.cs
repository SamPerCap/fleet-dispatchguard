using System.Data;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DispatchGuard.Infrastructure.Repositories;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly DispatchGuardDbContext _context;
    private readonly AdvisoryResourceLocks _locks;
    private bool _tripsSaved;

    private ITripRepository? _trips;
    private IBookingRepository? _bookings;
    private IDriverRepository? _drivers;
    private IVehicleRepository? _vehicles;
    private ILocationRepository? _locations;
    private ICircuitRepository? _circuits;
    private IClientRepository? _clients;
    private IBaseRepository? _bases;
    private ICachedRouteRepository? _cachedRoutes;

    private readonly Core.Interfaces.ITripDurationCache? _tripDurations;

    public UnitOfWork(DispatchGuardDbContext context, Core.Interfaces.ITripDurationCache? tripDurations = null, ResourceLockSettings? lockSettings = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _locks = new AdvisoryResourceLocks(context, lockSettings ?? new ResourceLockSettings());
        _tripDurations = tripDurations;
    }

    /// <summary>A saved trip change drops the A→B duration cache, only after the save succeeds (SPEC 0055 R5).</summary>
    private async Task<int> SaveAndInvalidateAsync(CancellationToken cancellationToken)
    {
        var tripsChanged = _context.ChangeTracker.Entries<Core.Entities.Trip>()
            .Any(e => e.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
        var saved = await SaveAsync(cancellationToken);
        _tripsSaved |= tripsChanged;
        if (tripsChanged) _tripDurations?.Invalidate();
        return saved;
    }

    private async Task<int> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AlreadyUsed(ex) is { } taken)
        {
            throw taken;
        }
    }

    /// <summary>
    /// Two requests at once pass the check of a value that cannot be repeated, and only the unique index stops the second one (SPEC 0079 R3).
    /// </summary>
    private static ValueAlreadyUsedException? AlreadyUsed(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return null;
        return ex.Entries.Select(e => e.Entity).FirstOrDefault(e => e is Driver or Vehicle or Client or OperatingBase) switch
        {
            Driver driver => ValueAlreadyUsedException.IdentificationNumber(driver.IdentificationNumber, ex),
            Vehicle vehicle => ValueAlreadyUsedException.LicensePlate(vehicle.LicensePlate, ex),
            Client client => ValueAlreadyUsedException.TaxId(client.TaxId, ex),
            OperatingBase operatingBase => ValueAlreadyUsedException.BaseName(operatingBase.Name, ex),
            _ => null
        };
    }

    #region Repository Properties (Lazy initialization)
    public ITripRepository Trips
    {
        get
        {
            _trips ??= new TripRepository(_context);
            return _trips;
        }
    }

    public IBookingRepository Bookings
    {
        get
        {
            _bookings ??= new BookingRepository(_context);
            return _bookings;
        }
    }

    public IDriverRepository Drivers
    {
        get
        {
            _drivers ??= new DriverRepository(_context);
            return _drivers;
        }
    }

    public IVehicleRepository Vehicles
    {
        get
        {
            _vehicles ??= new VehicleRepository(_context);
            return _vehicles;
        }
    }

    public ILocationRepository Locations
    {
        get
        {
            _locations ??= new LocationRepository(_context);
            return _locations;
        }
    }

    public ICircuitRepository Circuits
    {
        get
        {
            _circuits ??= new CircuitRepository(_context);
            return _circuits;
        }
    }

    public IClientRepository Clients
    {
        get
        {
            _clients ??= new ClientRepository(_context);
            return _clients;
        }
    }

    public IBaseRepository Bases => _bases ??= new BaseRepository(_context);

    public ICachedRouteRepository CachedRoutes
    {
        get
        {
            _cachedRoutes ??= new CachedRouteRepository(_context);
            return _cachedRoutes;
        }
    }
    #endregion
    
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await SaveAndInvalidateAsync(cancellationToken);
    }

    public async Task<T> ExecuteExclusiveAsync<T>(
        ResourceClaim claim,
        Func<CancellationToken, Task<T>> work,
        Func<T, Entity> written,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(written);
        if (_context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("There are unsaved changes and they would be lost: read and change inside the exclusive work");
        }

        _tripsSaved = false;
        Func<CancellationToken, Task<bool>>? isSaved = null;
        try
        {
            // EF's own retries (EnableRetryOnFailure) repeat the whole operation from what is saved.
            var result = await ExecutionStrategyExtensions.ExecuteInTransactionAsync<ResourceClaim, T>(
                _context.Database.CreateExecutionStrategy(),
                claim,
                async (needed, token) =>
                {
                    isSaved = null;
                    _context.ChangeTracker.Clear();
                    OperationTrace.Current?.Discarded(); // What an earlier try wrote went away with its transaction.
                    await _locks.AcquireAsync(needed, token);
                    var done = await work(token);
                    OperationTrace.Current?.Begin(TraceStages.Commit);
                    isSaved = SavedCheck(written(done));
                    return done;
                },
                (_, token) => isSaved is null ? Task.FromResult(false) : isSaved(token),
                (context, token) => context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token),
                cancellationToken);
            OperationTrace.Current?.End();
            if (_tripsSaved) _tripDurations?.Invalidate(); // Again, now that the others can see it (SPEC 0055).
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Rows that disappeared meanwhile (the demo reset, which wipes everything): the next try sees what is left.
            _context.ChangeTracker.Clear();
            throw new ResourceBusyException(string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name)), ex);
        }
        catch
        {
            _context.ChangeTracker.Clear(); // Nothing half-done may be saved later by someone else (e.g. the attempt log).
            throw;
        }
    }

    /// <summary>The row carries the timestamp this operation wrote: its commit was done even if the answer got lost.</summary>
    private Func<CancellationToken, Task<bool>> SavedCheck(Entity entity) => entity switch
    {
        Trip trip => Check<Trip>(trip),
        Driver driver => Check<Driver>(driver),
        Vehicle vehicle => Check<Vehicle>(vehicle),
        Booking booking => Check<Booking>(booking),
        _ => throw new ArgumentException($"Unexpected written entity {entity.GetType().Name}", nameof(entity))
    };

    private Func<CancellationToken, Task<bool>> Check<TEntity>(TEntity entity) where TEntity : Entity
    {
        var (id, stamp) = (entity.Id, entity.Audit.UpdatedAt);
        return token => _context.Set<TEntity>().AsNoTracking().AnyAsync(e => e.Id == id && e.Audit.UpdatedAt == stamp, token);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}