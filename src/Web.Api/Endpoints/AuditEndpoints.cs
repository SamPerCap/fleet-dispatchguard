using System.Text.Json;
using DispatchGuard.API.Auditing;
using DispatchGuard.API.Perimeter;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

/// <param name="Detail">Rules evaluated and queries, without their values.</param>
public sealed record RequestTraceDto(
    long Id, DateTime At, string TraceId, string Method, string Path, int Status, int DurationMs, int Queries, int QueriesMs, JsonElement Detail)
{
    public static RequestTraceDto Of(RequestTrace trace) => new(
        trace.Id, trace.At, trace.TraceId, trace.Method, trace.Path, trace.Status, trace.DurationMs, trace.Queries, trace.QueriesMs,
        JsonSerializer.Deserialize<JsonElement>(trace.Detail));
}

/// <summary>Only the author sees what is recorded, with a key that is not in the web (SPEC 0085 R7).</summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var audit = app.MapGroup("/api/audit").ExcludeFromDescription().AddEndpointFilter(OnlyTheAuthor);
        audit.MapGet("/requests/{id:long}", GetRequest).WithName("RequestTrace");
        audit.MapGet("/visits", GetVisits).WithName("Visits");
        audit.MapGet("/visits/{sessionId}", GetTimeline).WithName("VisitTimeline");
    }

    [NotTraced]
    private static async Task<Results<Ok<RequestTraceDto>, NotFound>> GetRequest(
        long id, IVisitReader visits, CancellationToken cancellationToken) =>
        await visits.RequestAsync(id, cancellationToken) is { } trace
            ? TypedResults.Ok(RequestTraceDto.Of(trace))
            : TypedResults.NotFound();

    [NotTraced]
    private static async Task<Ok<IReadOnlyList<VisitSummary>>> GetVisits(IVisitReader visits, CancellationToken cancellationToken) =>
        TypedResults.Ok(await visits.LastAsync(cancellationToken));

    [NotTraced]
    private static async Task<Ok<VisitTimeline>> GetTimeline(string sessionId, IVisitReader visits, CancellationToken cancellationToken) =>
        TypedResults.Ok(await visits.TimelineAsync(sessionId, cancellationToken));

    /// <summary>Without the key it answers as if nothing were there: a 401 would tell the public that there is something to open.</summary>
    private static async ValueTask<object?> OnlyTheAuthor(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.RequestServices.GetRequiredService<AuditKey>().IsIn(context.HttpContext.Request)
            ? await next(context)
            : TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: Texts.NotFoundTitle().In(CurrentLanguage.Value));
}
