using DispatchGuard.Application.DTOs.Client;
using DispatchGuard.Application.Services.Base;

namespace DispatchGuard.Application.Services.Clients;

public interface IClientService : ICrudService<ClientDto, CreateClientRequest, UpdateClientRequest>
{
    Task<IEnumerable<ClientDto>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ClientDto>> GetExternalAsync(CancellationToken cancellationToken = default);
    Task<ClientDto> ReactivateAsync(int id, CancellationToken cancellationToken = default);
}