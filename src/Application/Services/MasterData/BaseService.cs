using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;

namespace DispatchGuard.Application.Services.MasterData;

/// <summary>Names are unique.</summary>
public interface IBaseService
{
    Task<IReadOnlyList<BaseDto>> GetAllAsync(CancellationToken ct = default);
    Task<BaseDto> CreateAsync(BaseRequest request, CancellationToken ct = default);
    Task<BaseDto> RenameAsync(int id, BaseRequest request, CancellationToken ct = default);
}

public sealed class BaseService(IUnitOfWork uow) : IBaseService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));

    public async Task<IReadOnlyList<BaseDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _uow.Bases.GetAllAsync(ct)).OrderBy(b => b.Id).Select(b => new BaseDto(b.Id, b.Name)).ToList();

    public async Task<BaseDto> CreateAsync(BaseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = new OperatingBase(request.Name);
        await EnsureUniqueAsync(created.Name, null, ct);
        await _uow.Bases.AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return new BaseDto(created.Id, created.Name);
    }

    public async Task<BaseDto> RenameAsync(int id, BaseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await _uow.Bases.GetByIdAsync(id, ct);
        if (existing is not { IsActive: true }) throw MissingResource.Base(id);
        existing.Rename(request.Name);
        await EnsureUniqueAsync(existing.Name, id, ct);
        await _uow.Bases.UpdateAsync(existing, ct);
        await _uow.SaveChangesAsync(ct);
        return new BaseDto(existing.Id, existing.Name);
    }

    private async Task EnsureUniqueAsync(string name, int? exceptId, CancellationToken ct)
    {
        if (await _uow.Bases.ExistsByNameAsync(name, exceptId, ct)) throw ValueAlreadyUsedException.BaseName(name);
    }
}
