using DispatchGuard.Core.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", GetLive).WithTags("Health").ShortCircuit();
        app.MapGet("/health/ready", GetReady).WithTags("Health");
    }

    /// <summary>
    /// The only one for the probes of the host: the other wakes the database up, and asks for a session (SPEC 0086). Short-circuited,
    /// because a probe comes every few seconds without HTTPS.
    /// </summary>
    private static Ok GetLive(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, StatusCodeHttpResult>> GetReady(HttpResponse response, IDatabaseReadiness database)
    {
        response.Headers.CacheControl = "no-store";
        return await database.IsReadyAsync() ? TypedResults.Ok() : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
