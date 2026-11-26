using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.MasterData;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class BasesEndpoints
{
    public static void MapBasesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bases").WithTags("Bases");

        group.MapGet("/", GetBases);
        group.MapPost("/", CreateBase);
        group.MapPut("/{id:int}", RenameBase);
    }

    private static async Task<Ok<IReadOnlyList<BaseDto>>> GetBases(IBaseService bases, CancellationToken cancellationToken) =>
        TypedResults.Ok(await bases.GetAllAsync(cancellationToken));

    private static async Task<Created<BaseDto>> CreateBase(
        BaseRequest request, IBaseService bases, CancellationToken cancellationToken)
    {
        var created = await bases.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/bases/{created.Id}", created);
    }

    private static async Task<Ok<BaseDto>> RenameBase(
        int id, BaseRequest request, IBaseService bases, CancellationToken cancellationToken) =>
        TypedResults.Ok(await bases.RenameAsync(id, request, cancellationToken));
}
