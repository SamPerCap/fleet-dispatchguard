using AutoMapper;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace DispatchGuard.Application.Services.Base;

public abstract class CrudService<TEntity, TDto, TCreateDto, TUpdateDto> : ICrudService<TDto, TCreateDto, TUpdateDto>
    where TEntity : Entity
    where TDto : class
    where TCreateDto : class
    where TUpdateDto : class
{
    protected IUnitOfWork UnitOfWork { get; }
    protected IMapper Mapper { get; }
    protected ILogger Logger { get; }

    protected CrudService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger logger)
    {
        UnitOfWork = unitOfWork;
        Mapper = mapper;
        Logger = logger;
    }

    protected abstract IGenericRepository<TEntity> GetRepository();

    protected abstract TEntity MapCreateDtoToEntity(TCreateDto createDto);

    protected abstract void UpdateEntityFromDto(TEntity entity, TUpdateDto updateDto);

    /// <summary>Before saving it, new or changed. Throws if it has a value that another one already has.</summary>
    protected virtual Task EnsureUniqueAsync(TEntity entity, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>For the log, in English.</summary>
    protected abstract string EntityName { get; }

    protected abstract KeyNotFoundException NotFoundError(int id);

    public virtual async Task<IEnumerable<TDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await GetRepository().GetAllAsync(cancellationToken);
        return Mapper.Map<IEnumerable<TDto>>(entities);
    }

    public virtual async Task<TDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetRepository().GetByIdAsync(id, cancellationToken);
        return entity == null ? null : Mapper.Map<TDto>(entity);
    }

    public virtual async Task<TDto> CreateAsync(TCreateDto request, CancellationToken cancellationToken = default)
    {
        var entity = MapCreateDtoToEntity(request);
        await EnsureUniqueAsync(entity, cancellationToken);

        await GetRepository().AddAsync(entity, cancellationToken);
        await UnitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Created {EntityName} {EntityId}", EntityName, entity.Id);

        return Mapper.Map<TDto>(entity);
    }

    public virtual async Task<TDto> UpdateAsync(int id, TUpdateDto request, CancellationToken cancellationToken = default)
    {
        var entity = await GetRepository().GetByIdAsync(id, cancellationToken);

        if (entity == null)
        {
            throw NotFoundError(id);
        }

        UpdateEntityFromDto(entity, request);
        await EnsureUniqueAsync(entity, cancellationToken);

        await GetRepository().UpdateAsync(entity, cancellationToken);
        await UnitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Updated {EntityName} {EntityId}", EntityName, id);

        return Mapper.Map<TDto>(entity);
    }

    public virtual async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetRepository().GetByIdAsync(id, cancellationToken);

        if (entity == null)
        {
            throw NotFoundError(id);
        }

        entity.Deactivate();
        await GetRepository().UpdateAsync(entity, cancellationToken);
        await UnitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Deactivated {EntityName} {EntityId}", EntityName, id);
    }
}