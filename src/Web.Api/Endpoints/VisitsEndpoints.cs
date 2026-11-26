using System.ComponentModel.DataAnnotations;
using DispatchGuard.API.Auditing;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

/// <param name="AgoMs">Since it happened.</param>
/// <param name="Name">One of the list of the web.</param>
public sealed record VisitEventRequest(
    int AgoMs,
    VisitEventKind Kind,
    [property: Required(ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.InvalidVisitName)), RegularExpression(@"^[\p{L}\p{N} .,:/()«»_+-]{1,80}$", ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.InvalidVisitName))] string Name,
    [property: MaxLength(RequestTrace.TraceIdLength)] string? TraceId,
    [property: Range(0, 599)] int? Status,
    int? DurationMs);

public sealed record VisitBatchRequest(
    [property: MaxLength(VisitBatchRequest.LongestReference)] string? Reference,
    [property: Required, MaxLength(VisitBatchRequest.MaxEvents)] IReadOnlyList<VisitEventRequest> Events)
{
    public const int MaxEvents = 50;

    /// <summary>More than any known one: a strange code is ignored, not refused.</summary>
    public const int LongestReference = 200;
}

public static class VisitsEndpoints
{
    /// <summary>A batch leaves long before, and an older instant would fall outside the visit.</summary>
    private const int LongestMs = 10 * 60 * 1_000;

    public static void MapVisitsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/visits", RecordVisit).WithName("RecordVisit").WithTags("Visits");
    }

    [RecordsVisit]
    [NotTraced]
    private static async Task<NoContent> RecordVisit(
        VisitBatchRequest batch, HttpContext context, VisitSettings settings, VisitNotice notice, IVisitLog log, CancellationToken cancellationToken)
    {
        if (AnonymousSession.IdOf(context.User) is not { } session)
        {
            // Only where sessions are turned off: there is no visit to speak of.
            return TypedResults.NoContent();
        }

        var reference = settings.KnownReference(batch.Reference);
        List<VisitEventEntry> events = [.. batch.Events.Where(e => e.Kind != VisitEventKind.Arrived).Select(ToEntry)];
        if (reference is not null)
        {
            events.Insert(0, new VisitEventEntry(TimeSpan.Zero, VisitEventKind.Arrived, reference));
        }

        if (await log.RecordAsync(session, reference, events, cancellationToken))
        {
            // Waited for, because the host may stop the process afterwards; the web does not wait for this answer.
            await notice.SendAsync(AnonymousSession.ShortIdOf(session), reference);
        }

        return TypedResults.NoContent();
    }

    private static VisitEventEntry ToEntry(VisitEventRequest request) => new(
        TimeSpan.FromMilliseconds(Math.Clamp(request.AgoMs, 0, LongestMs)),
        request.Kind,
        request.Name,
        RequestThread.IsTraceId(request.TraceId) ? request.TraceId : null,
        request.Status,
        request.DurationMs is { } duration ? Math.Clamp(duration, 0, LongestMs) : null);
}
