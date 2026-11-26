using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Repositories;

/// <summary>
/// Concrete repositories only add their own queries, or override <see cref="Query"/> to load the parts of the aggregate their
/// consumers need (SPEC 0034).
/// <list type="bullet">
/// <item>Never saves: writes are only tracked; <see cref="IUnitOfWork.SaveChangesAsync"/> confirms them.</item>
/// <item><see cref="DeleteAsync"/> is a soft delete through <see cref="Entity.Deactivate"/>.</item>
/// <item><see cref="GetAllAsync"/> skips inactive rows; <see cref="GetByIdAsync"/> does not.</item>
/// </list>
/// </summary>
public abstract class EfRepository<T> : IGenericRepository<T> where T : Entity
{
    protected DispatchGuardDbContext Context { get; }

    protected DbSet<T> Set { get; }

    protected EfRepository(DispatchGuardDbContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Set = context.Set<T>();
    }

    /// <summary>Override to <c>Include</c> what the aggregate needs.</summary>
    protected virtual IQueryable<T> Query() => Set;

    public virtual Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Query().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Query().Where(e => e.IsActive).ToListAsync(cancellationToken);

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Set.AddAsync(entity, cancellationToken);
        return entity;
    }

    public virtual Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        TrackAsModified(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.Deactivate();
        TrackAsModified(entity);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A tracked entity is already followed by the change tracker; only a detached one
    /// (loaded by another context) is attached as modified.
    /// </summary>
    private void TrackAsModified(T entity)
    {
        if (Context.Entry(entity).State == EntityState.Detached)
        {
            Set.Update(entity);
        }
    }
}
