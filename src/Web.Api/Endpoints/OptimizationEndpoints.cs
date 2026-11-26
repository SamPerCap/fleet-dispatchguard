using DispatchGuard.API.Perimeter;
using DispatchGuard.Application.Services.Optimization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class OptimizationEndpoints
{
    public static void MapOptimizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/optimization/vehicle-chaining", ProposeVehicleChaining)
            .WithName("ProposeVehicleChaining")
            .WithTags("Optimization")
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    [HeavyCalculation]
    private static async Task<Ok<VehicleChainingDto>> ProposeVehicleChaining(
        DayOrInstant from, DayOrInstant to, bool? returnToBase,
        IVehicleChainingService vehicleChaining, CancellationToken cancellationToken) =>
        TypedResults.Ok(await vehicleChaining.Propose(from.Utc, to.Utc, returnToBase ?? true, cancellationToken));
}
