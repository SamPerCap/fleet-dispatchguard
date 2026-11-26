using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services.MasterData;
using DispatchGuard.Application.Services.TravelTime;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class LocationsEndpoints
{
    private const string GetLocationByIdRoute = "GetLocationById";

    public static void MapLocationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locations").WithTags("Locations");

        group.MapGet("/", GetLocations);
        group.MapGet("/{id:int}", GetLocation).WithName(GetLocationByIdRoute);
        group.MapGet("/travel-time", GetTravelTime)
            .WithName("GetTravelTime")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateLocation);
        group.MapDelete("/{id:int}", DeleteLocation).BlockedByMasterDataDeletionLock();
    }

    private static async Task<Ok<IReadOnlyList<LocationDto>>> GetLocations(
        ILocationService locations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await locations.GetAllAsync(cancellationToken));

    private static async Task<Ok<LocationDto>> GetLocation(
        int id, ILocationService locations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await locations.GetAsync(id, cancellationToken));

    private static async Task<Ok<TravelTimeEstimate>> GetTravelTime(
        int originId, int destinationId, ITravelTimeService travelTime, CancellationToken cancellationToken) =>
        TypedResults.Ok(await travelTime.GetAsync(originId, destinationId, cancellationToken));

    private static async Task<CreatedAtRoute<LocationDto>> CreateLocation(
        CreateLocationRequest request, ILocationService locations, CancellationToken cancellationToken)
    {
        var created = await locations.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetLocationByIdRoute, new { id = created.Id });
    }

    private static async Task<NoContent> DeleteLocation(
        int id, ILocationService locations, CancellationToken cancellationToken)
    {
        await locations.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
