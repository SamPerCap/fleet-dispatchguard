using DispatchGuard.Application.DTOs.Client;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Clients;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class ClientsEndpoints
{
    private const string GetClientByIdRoute = "GetClientById";

    public static void MapClientsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clients").WithTags("Clients");

        group.MapGet("/", GetClients).WithName("GetAllClients");
        group.MapGet("/active", GetActiveClients).WithName("GetActiveClients");
        group.MapGet("/external", GetExternalClients).WithName("GetExternalClients");
        group.MapGet("/{id:int}", GetClient).WithName(GetClientByIdRoute);
        group.MapPost("/", CreateClient).WithName("CreateClient");
        group.MapPut("/{id:int}", UpdateClient).WithName("UpdateClient");
        group.MapDelete("/{id:int}", DeleteClient).WithName("DeleteClient");
        group.MapPost("/{id:int}/reactivate", ReactivateClient).WithName("ReactivateClient");
    }

    private static async Task<Ok<IEnumerable<ClientDto>>> GetClients(
        IClientService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.GetAllAsync(cancellationToken));

    private static async Task<Ok<IEnumerable<ClientDto>>> GetActiveClients(
        IClientService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.GetActiveAsync(cancellationToken));

    private static async Task<Ok<IEnumerable<ClientDto>>> GetExternalClients(
        IClientService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.GetExternalAsync(cancellationToken));

    private static async Task<Ok<ClientDto>> GetClient(
        int id, IClientService clients, CancellationToken cancellationToken)
    {
        var client = await clients.GetByIdAsync(id, cancellationToken)
            ?? throw MissingResource.Client(id);
        return TypedResults.Ok(client);
    }

    private static async Task<CreatedAtRoute<ClientDto>> CreateClient(
        CreateClientRequest request, IClientService clients, CancellationToken cancellationToken)
    {
        var created = await clients.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetClientByIdRoute, new { id = created.Id });
    }

    private static async Task<Ok<ClientDto>> UpdateClient(
        int id, UpdateClientRequest request, IClientService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteClient(
        int id, IClientService clients, CancellationToken cancellationToken)
    {
        await clients.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ClientDto>> ReactivateClient(
        int id, IClientService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.ReactivateAsync(id, cancellationToken));
}
