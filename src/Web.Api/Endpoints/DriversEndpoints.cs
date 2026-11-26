using DispatchGuard.API.Http;
using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.MasterData;
using DispatchGuard.Application.Services.Resources;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class DriversEndpoints
{
    private const string GetDriverByIdRoute = "GetDriverById";

    public static void MapDriversEndpoints(this IEndpointRouteBuilder app)
    {
        // 503: another operation may be holding the driver; the client can retry (SPEC 0065).
        var group = app.MapGroup("/api/drivers")
            .WithTags("Drivers")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", GetDrivers);
        group.MapGet("/{id:int}", GetDriver).WithName(GetDriverByIdRoute);
        group.MapPost("/", CreateDriver);
        group.MapPut("/{id:int}", UpdateDriver)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:int}", DeleteDriver).BlockedByMasterDataDeletionLock();
        group.MapPut("/{id:int}/last-weekly-rest", DeclareLastWeeklyRest);
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
        group.MapGet("/{id:int}/hours", GetHours)
            .WithName("GetDriverHours")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:int}/history", GetHistory)
            .WithName("GetDriverHistory")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<IReadOnlyList<DriverDto>>> GetDrivers(
        IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.GetAllAsync(cancellationToken));

    private static async Task<Ok<DriverDto>> GetDriver(
        int id, IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.GetAsync(id, cancellationToken));

    private static async Task<CreatedAtRoute<DriverDto>> CreateDriver(
        CreateDriverRequest request, IDriverService drivers, CancellationToken cancellationToken)
    {
        var created = await drivers.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetDriverByIdRoute, new { id = created.Id });
    }

    private static async Task<Ok<DriverDto>> UpdateDriver(
        int id, UpdateDriverRequest request, IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteDriver(
        int id, IDriverService drivers, CancellationToken cancellationToken)
    {
        await drivers.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<DriverDto>> DeclareLastWeeklyRest(
        int id, DeclareWeeklyRestRequest request, IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.DeclareLastWeeklyRestAsync(id, request.RestEnd, cancellationToken));

    private static async Task<Ok<DriverDto>> AddUnavailability(
        int id, CreateDriverUnavailabilityRequest request, IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.AddUnavailabilityAsync(id, request, cancellationToken));

    private static async Task<Ok<DriverDto>> ChangeUnavailability(
        int id, int unavailabilityId, UpdateDriverUnavailabilityRequest request, IDriverService drivers, CancellationToken cancellationToken) =>
        TypedResults.Ok(await drivers.ChangeUnavailabilityAsync(id, unavailabilityId, request, cancellationToken));

    private static async Task<NoContent> RemoveUnavailability(
        int id, int unavailabilityId, IDriverService drivers, CancellationToken cancellationToken)
    {
        await drivers.RemoveUnavailabilityAsync(id, unavailabilityId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<DriverHoursDto>> GetHours(
        int id, DateTime? at, IDriverHoursQuery driverHours, CancellationToken cancellationToken) =>
        TypedResults.Ok(await driverHours.GetAsync(id, at.AsUtc() ?? DateTime.UtcNow, cancellationToken));

    private static async Task<Ok<IReadOnlyList<AffectedTripDto>>> GetAffectedTrips(
        int id, DateTime from, DateTime to, IAffectedTripsQuery affectedTrips, CancellationToken cancellationToken) =>
        TypedResults.Ok(await affectedTrips.DriverAsync(id, from.AsUtc(), to.AsUtc(), cancellationToken));

    private static async Task<Ok<ResourceHistoryDto>> GetHistory(
        int id, DateTime? at, int? page, int? pageSize,
        IResourceHistoryService resourceHistory, CancellationToken cancellationToken)
    {
        var history = await resourceHistory.DriverAsync(
            id,
            at.AsUtc() ?? DateTime.UtcNow,
            page ?? ResourceHistoryService.DefaultPage,
            pageSize ?? ResourceHistoryService.DefaultPageSize,
            cancellationToken);
        return TypedResults.Ok(history);
    }
}
