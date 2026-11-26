using DispatchGuard.API.Http;
using DispatchGuard.Application.Services.Control;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

/// <summary>Without <c>baseId</c> every query covers all the bases.</summary>
public static class ControlEndpoints
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int DefaultEngineMetricsDays = 7;

    public static void MapControlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/control").WithTags("Control");

        group.MapGet("/bases", GetBases).WithName("ControlBases");
        group.MapGet("/summary", GetSummary).WithName("ControlSummary");
        group.MapGet("/alerts", GetAlerts).WithName("ControlAlerts");
        group.MapGet("/incidents", GetIncidents).WithName("ControlIncidents");
        group.MapGet("/resources", GetResources).WithName("ControlResources");
        group.MapGet("/engine-metrics", GetEngineMetrics).WithName("ControlEngineMetrics");
        group.MapGet("/activity", GetActivity).WithName("ControlActivity");
        group.MapGet("/trends", GetTrends).WithName("ControlTrends");
        group.MapGet("/engine-hourly", GetEngineHourly).WithName("ControlEngineHourly");
        group.MapGet("/efficiency", GetEfficiency).WithName("ControlEfficiency");
    }

    private static async Task<Ok<IReadOnlyList<BaseDto>>> GetBases(
        IControlCenterService controlCenter, CancellationToken cancellationToken) =>
        TypedResults.Ok(await controlCenter.BasesAsync(cancellationToken));

    private static async Task<Ok<ControlSummaryDto>> GetSummary(
        int? baseId, DateTime? date, IControlCenterService controlCenter, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return TypedResults.Ok(await controlCenter.SummaryAsync(baseId, date.AsUtc() ?? now, now, cancellationToken));
    }

    private static async Task<Ok<ControlPageDto<ControlAlertDto>>> GetAlerts(
        int? baseId, DateTime? date, int? page, int? pageSize,
        IControlCenterService controlCenter, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var alerts = await controlCenter.AlertsAsync(
            baseId, date.AsUtc() ?? now, now, page ?? DefaultPage, pageSize ?? DefaultPageSize, cancellationToken);
        return TypedResults.Ok(alerts);
    }

    private static async Task<Ok<IReadOnlyList<ControlIncidentDto>>> GetIncidents(
        int? baseId, DateTime? date, IControlCenterService controlCenter, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return TypedResults.Ok(await controlCenter.IncidentsAsync(baseId, date.AsUtc() ?? now, now, cancellationToken));
    }

    private static async Task<Ok<ControlResourcesDto>> GetResources(
        int? baseId, DateTime? at, IControlCenterService controlCenter, CancellationToken cancellationToken) =>
        TypedResults.Ok(await controlCenter.ResourcesAsync(baseId, at.AsUtc() ?? DateTime.UtcNow, cancellationToken));

    private static async Task<Ok<EngineMetricsDto>> GetEngineMetrics(
        int? baseId, DateTime? from, DateTime? to, IControlCenterService controlCenter, CancellationToken cancellationToken)
    {
        var end = to.AsUtc() ?? DateTime.UtcNow;
        var start = from.AsUtc() ?? end.AddDays(-DefaultEngineMetricsDays);
        return TypedResults.Ok(await controlCenter.EngineMetricsAsync(baseId, start, end, cancellationToken));
    }

    private static async Task<Ok<ControlPageDto<ActivityItemDto>>> GetActivity(
        int? baseId, DateTime? since, int? page, int? pageSize, ActivityKind? kind,
        IControlCenterService controlCenter, CancellationToken cancellationToken)
    {
        var activity = await controlCenter.ActivityAsync(
            baseId,
            since.AsUtc() ?? DateTime.UtcNow.AddDays(-1),
            page ?? DefaultPage,
            pageSize ?? DefaultPageSize,
            kind ?? ActivityKind.All,
            cancellationToken);
        return TypedResults.Ok(activity);
    }

    private static async Task<Ok<ControlTrendDto>> GetTrends(
        int? baseId, DateTime? date, int? days, IControlInsightsService insights, CancellationToken cancellationToken)
    {
        var trends = await insights.TrendsAsync(
            baseId, date.AsUtc() ?? DateTime.UtcNow, days ?? ControlTrends.DefaultDays, cancellationToken);
        return TypedResults.Ok(trends);
    }

    private static async Task<Ok<EngineHourlyDto>> GetEngineHourly(
        int? baseId, DateTime? date, IControlInsightsService insights, CancellationToken cancellationToken) =>
        TypedResults.Ok(await insights.EngineHourlyAsync(baseId, date.AsUtc() ?? DateTime.UtcNow, cancellationToken));

    private static async Task<Ok<ControlEfficiencyDto>> GetEfficiency(
        int? baseId, DateTime? date, IControlInsightsService insights, CancellationToken cancellationToken) =>
        TypedResults.Ok(await insights.EfficiencyAsync(baseId, date.AsUtc() ?? DateTime.UtcNow, cancellationToken));
}
