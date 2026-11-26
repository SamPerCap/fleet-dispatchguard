using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.Engine;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class EngineEndpoints
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;

    public static void MapEngineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/engine").WithTags("Engine");

        group.MapGet("/attempts", GetAttempts).WithName("EngineAttempts");
        group.MapGet("/attempts/{id:int}", GetAttempt).WithName("EngineAttempt");
        group.MapGet("/hourly", GetHourly).WithName("EngineHourly");
    }

    private static async Task<Ok<EngineAuditPageDto>> GetAttempts(
        [AsParameters] EngineAuditFilter filter, ActivityKind? kind, int? page, int? pageSize,
        IEngineAuditService engineAudit, CancellationToken cancellationToken)
    {
        var query = filter.ToQuery(kind ?? ActivityKind.All);
        var attempts = await engineAudit.AttemptsAsync(query, page ?? DefaultPage, pageSize ?? DefaultPageSize, cancellationToken);
        return TypedResults.Ok(attempts);
    }

    private static async Task<Ok<EngineAttemptDetailDto>> GetAttempt(
        int id, IEngineAuditService engineAudit, CancellationToken cancellationToken) =>
        TypedResults.Ok(await engineAudit.AttemptAsync(id, cancellationToken));

    private static async Task<Ok<EngineAuditHourlyDto>> GetHourly(
        [AsParameters] EngineAuditFilter filter, IEngineAuditService engineAudit, CancellationToken cancellationToken) =>
        TypedResults.Ok(await engineAudit.HourlyAsync(filter.ToQuery(ActivityKind.All), cancellationToken));
}
