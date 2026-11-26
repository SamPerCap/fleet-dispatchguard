using DispatchGuard.API.XRay;
using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.MasterData;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class TripsEndpoints
{
    private const string GetTripByIdRoute = "GetTripById";

    public static void MapTripsEndpoints(this IEndpointRouteBuilder app)
    {
        // 503: another operation may be holding the trip; the client can retry (SPEC 0065).
        var group = app.MapGroup("/api/trips")
            .WithTags("Trips")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", GetTrips);
        group.MapGet("/{id:int}", GetTrip).WithName(GetTripByIdRoute);
        group.MapPost("/", CreateTrip);
        group.MapPost("/{id:int}/cancel", CancelTrip);
        group.MapPost("/{tripId:int}/allocate", AllocateTrip)
            .WithName("AllocateDriverToTrip")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/{tripId:int}/driver-candidates", FindDriverCandidates)
            .WithName("FindDriverCandidates")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{tripId:int}/driver-options", FindDriverOptions)
            .WithName("FindDriverOptions")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{tripId:int}/vehicle-candidates", FindVehicleCandidates)
            .WithName("FindVehicleCandidates")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<PagedDto<TripDto>>> GetTrips(
        DateTime from, DateTime to, ITripService trips, CancellationToken cancellationToken,
        int page = 1, int pageSize = 50) =>
        TypedResults.Ok(await trips.GetByRangeAsync(from, to, page, pageSize, cancellationToken));

    private static async Task<Ok<TripDto>> GetTrip(
        int id, ITripService trips, CancellationToken cancellationToken) =>
        TypedResults.Ok(await trips.GetAsync(id, cancellationToken));

    private static async Task<CreatedAtRoute<TripDto>> CreateTrip(
        CreateTripRequest request, ITripService trips, CancellationToken cancellationToken)
    {
        var created = await trips.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetTripByIdRoute, new { id = created.Id });
    }

    private static async Task<Ok<TripDto>> CancelTrip(
        int id, ITripService trips, CancellationToken cancellationToken) =>
        TypedResults.Ok(await trips.CancelAsync(id, cancellationToken));

    [XRayed(XRayKinds.Allocation)]
    private static async Task<Ok<AllocationResponse>> AllocateTrip(
        int tripId,
        AllocationRequest request,
        IDriverAllocationService allocation,
        CancellationToken cancellationToken)
    {
        // The body accepts any number as an enum, so an undefined origin would reach the audit log.
        if (!Enum.IsDefined(request.Origin))
        {
            throw new InvalidRequestException(Texts.UnknownValue(nameof(request.Origin), request.Origin.ToString()));
        }

        var requestedOverride = request.ToOverride();
        var result = await allocation.AllocateDriver(
            tripId,
            request.DriverId,
            request.VehicleId,
            cancellationToken,
            request.AssignedBy,
            requestedOverride,
            request.Origin,
            request.OnlyIfUnassigned);

        return TypedResults.Ok(AllocationResponse.From(tripId, result, requestedOverride));
    }

    private static async Task<Ok<List<DriverCandidateDto>>> FindDriverCandidates(
        int tripId, IDriverCandidateSearch search, CancellationToken cancellationToken,
        int maxResults = DriverCandidateSearch.DefaultMaxResults)
    {
        var candidates = await search.FindCandidatesForTrip(tripId, maxResults, cancellationToken);
        return TypedResults.Ok(DriverCandidateDto.FromRanked(candidates));
    }

    private static async Task<Ok<DriverOptionsDto>> FindDriverOptions(
        int tripId, IDriverCandidateSearch search, CancellationToken cancellationToken)
    {
        var options = await search.FindOptionsForTrip(tripId, cancellationToken);
        return TypedResults.Ok(DriverOptionsDto.From(options));
    }

    private static async Task<Ok<List<VehicleCandidateDto>>> FindVehicleCandidates(
        int tripId, IVehicleCandidateSearch search, CancellationToken cancellationToken,
        int maxResults = VehicleCandidateSearch.DefaultMaxResults)
    {
        var candidates = await search.FindCandidatesForTrip(tripId, maxResults, cancellationToken);
        return TypedResults.Ok(VehicleCandidateDto.FromRanked(candidates));
    }
}
