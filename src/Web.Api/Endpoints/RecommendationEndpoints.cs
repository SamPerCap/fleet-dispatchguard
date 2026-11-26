using DispatchGuard.API.Http;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.XRay;
using DispatchGuard.Application.Services.Recommendation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

/// <summary>
/// Nothing here writes or records an attempt, not even the POSTs: they only carry a body. A recommendation is applied trip by trip
/// through the allocate endpoint, so every line is validated again (ADR 0020).
/// </summary>
public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recommendations")
            .WithTags("Recommendations")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", ProposeRecommendation).WithName("ProposeRecommendation");
        group.MapPost("/check", CheckRecommendation).WithName("CheckRecommendation");
        group.MapPost("/options", GetOptions).WithName("RecommendationOptions");

        app.MapGet("/api/trips/{tripId:int}/recommendation", GetTripRecommendation)
            .WithName("TripRecommendation")
            .WithTags("Recommendations")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    [HeavyCalculation]
    [XRayed(XRayKinds.Recommendation)]
    private static async Task<Ok<RecommendationDto>> ProposeRecommendation(
        RecommendationRequest request, IRecommendationService recommendations, CancellationToken cancellationToken)
    {
        var requestInUtc = request with { From = request.From.AsUtc(), To = request.To.AsUtc() };
        return TypedResults.Ok(await recommendations.ProposeAsync(requestInUtc, cancellationToken));
    }

    [HeavyCalculation]
    [XRayed(XRayKinds.Recommendation)]
    private static async Task<Ok<IReadOnlyList<RecommendationLineDto>>> CheckRecommendation(
        RecommendationCheckRequest request, IRecommendationService recommendations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await recommendations.CheckAsync(request, cancellationToken));

    [HeavyCalculation]
    [XRayed(XRayKinds.Recommendation)]
    private static async Task<Ok<RecommendationOptionsDto>> GetOptions(
        RecommendationOptionsRequest request, IRecommendationService recommendations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await recommendations.OptionsAsync(request, cancellationToken));

    private static async Task<Ok<TripRecommendationDto>> GetTripRecommendation(
        int tripId, IRecommendationService recommendations, CancellationToken cancellationToken) =>
        TypedResults.Ok(await recommendations.ForTripAsync(tripId, cancellationToken));
}
