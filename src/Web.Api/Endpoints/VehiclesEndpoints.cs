using DispatchGuard.API.Http;
using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services.MasterData;
using DispatchGuard.Application.Services.Resources;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class VehiclesEndpoints
{
    private const string GetVehicleByIdRoute = "GetVehicleById";

    public static void MapVehiclesEndpoints(this IEndpointRouteBuilder app)
    {
        // 503: another operation may be holding the vehicle; the client can retry (SPEC 0065).
        var group = app.MapGroup("/api/vehicles")
            .WithTags("Vehicles")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", GetVehicles);
        group.MapGet("/{id:int}", GetVehicle).WithName(GetVehicleByIdRoute);
        group.MapPost("/", CreateVehicle);
        group.MapPut("/{id:int}", UpdateVehicle)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:int}", DeleteVehicle).BlockedByMasterDataDeletionLock();
        group.MapPost("/{id:int}/unavailabilities", AddUnavailability);
        group.MapPut("/{id:int}/unavailabilities/{unavailabilityId:int}", ChangeUnavailability)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:int}/unavailabilities/{unavailabilityId:int}", RemoveUnavailability)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:int}/affected-trips", GetAffectedTrips)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:int}/history", GetHistory)
            .WithName("GetVehicleHistory")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<VehicleDto>>> GetVehicles(
        IVehicleService vehicles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicles.GetAllAsync(cancellationToken));

    private static async Task<Ok<VehicleDto>> GetVehicle(
        int id, IVehicleService vehicles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicles.GetAsync(id, cancellationToken));

    private static async Task<CreatedAtRoute<VehicleDto>> CreateVehicle(
        CreateVehicleRequest request, IVehicleService vehicles, CancellationToken cancellationToken)
    {
        var created = await vehicles.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetVehicleByIdRoute, new { id = created.Id });
    }

    private static async Task<Ok<VehicleDto>> UpdateVehicle(
        int id, UpdateVehicleRequest request, IVehicleService vehicles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicles.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteVehicle(
        int id, IVehicleService vehicles, CancellationToken cancellationToken)
    {
        await vehicles.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<VehicleDto>> AddUnavailability(
        int id, CreateVehicleUnavailabilityRequest request, IVehicleService vehicles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicles.AddUnavailabilityAsync(id, request, cancellationToken));

    private static async Task<Ok<VehicleDto>> ChangeUnavailability(
        int id, int unavailabilityId, UpdateVehicleUnavailabilityRequest request, IVehicleService vehicles, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicles.ChangeUnavailabilityAsync(id, unavailabilityId, request, cancellationToken));

    private static async Task<NoContent> RemoveUnavailability(
        int id, int unavailabilityId, IVehicleService vehicles, CancellationToken cancellationToken)
    {
        await vehicles.RemoveUnavailabilityAsync(id, unavailabilityId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<AffectedTripDto>>> GetAffectedTrips(
        int id, DateTime from, DateTime to, IAffectedTripsQuery affectedTrips, CancellationToken cancellationToken) =>
        TypedResults.Ok(await affectedTrips.VehicleAsync(id, from.AsUtc(), to.AsUtc(), cancellationToken));

    private static async Task<Ok<ResourceHistoryDto>> GetHistory(
        int id, DateTime? at, int? page, int? pageSize,
        IResourceHistoryService resourceHistory, CancellationToken cancellationToken)
    {
        var history = await resourceHistory.VehicleAsync(
            id,
            at.AsUtc() ?? DateTime.UtcNow,
            page ?? ResourceHistoryService.DefaultPage,
            pageSize ?? ResourceHistoryService.DefaultPageSize,
            cancellationToken);
        return TypedResults.Ok(history);
    }
}
