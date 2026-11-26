using AutoMapper;
using DispatchGuard.Application.DTOs.Client;
using DispatchGuard.Application.Services.Base;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace DispatchGuard.Application.Services.Clients;

public class ClientService : CrudService<Client, ClientDto, CreateClientRequest, UpdateClientRequest>, IClientService
{
    public ClientService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<ClientService> logger)
        : base(unitOfWork, mapper, logger)
    {
    }

    protected override string EntityName => "Client";

    protected override KeyNotFoundException NotFoundError(int id) => MissingResource.Client(id);

    protected override IGenericRepository<Client> GetRepository() => UnitOfWork.Clients;

    protected override Client MapCreateDtoToEntity(CreateClientRequest createDto)
    {
        return new Client(
            createDto.Name,
            createDto.IsExternal,
            createDto.TaxId,
            createDto.ContactEmail,
            createDto.ContactPhone
        );
    }

    protected override void UpdateEntityFromDto(Client entity, UpdateClientRequest updateDto)
    {
        entity.Rename(updateDto.Name);
        entity.UpdateContactInfo(updateDto.ContactEmail, updateDto.ContactPhone);

        if (!string.IsNullOrWhiteSpace(updateDto.TaxId))
        {
            entity.UpdateTaxId(updateDto.TaxId);
        }
    }

    protected override async Task EnsureUniqueAsync(Client entity, CancellationToken cancellationToken)
    {
        if (entity.TaxId is { } taxId && await UnitOfWork.Clients.ExistsByTaxIdAsync(taxId, entity.Id, cancellationToken))
        {
            throw ValueAlreadyUsedException.TaxId(taxId);
        }
    }

    public async Task<IEnumerable<ClientDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var clients = await UnitOfWork.Clients.GetActiveClientsAsync(cancellationToken);
        return Mapper.Map<IEnumerable<ClientDto>>(clients);
    }

    public async Task<IEnumerable<ClientDto>> GetExternalAsync(CancellationToken cancellationToken = default)
    {
        var clients = await UnitOfWork.Clients.GetExternalClientsAsync(cancellationToken);
        return Mapper.Map<IEnumerable<ClientDto>>(clients);
    }

    public async Task<ClientDto> ReactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await UnitOfWork.Clients.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            throw MissingResource.Client(id);
        }

        client.Reactivate();
        await UnitOfWork.Clients.UpdateAsync(client, cancellationToken);
        await UnitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogInformation("Reactivated Client {ClientId}", id);

        return Mapper.Map<ClientDto>(client);
    }
}