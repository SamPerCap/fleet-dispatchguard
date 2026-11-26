using DispatchGuard.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DispatchGuard.API.Errors;

/// <summary>The single place that turns exceptions into <see cref="ProblemDetails"/>; a 500 never carries internal details (SPEC 0035).</summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = Map(exception);
        if (problem.Status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        // Not a rejection: the client may simply try again (SPEC 0065 R5).
        if (exception is ResourceBusyException busy)
        {
            logger.LogWarning("{Resource} was busy for too long on {Method} {Path}", busy.Resource, httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.Headers.RetryAfter = "1";
        }

        if (exception is AttemptNotRecordedException)
        {
            logger.LogError(exception, "{Path} was refused because it could not be recorded", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    internal static ProblemDetails Map(Exception exception) => WithExplanation(MapProblem(exception), exception);

    private static ProblemDetails WithExplanation(ProblemDetails problem, Exception exception)
    {
        var explanation = exception switch
        {
            DriverUnavailableException ex => ex.Explanation,
            ComplianceException ex => ex.Explanation,
            VehicleIneligibleException ex => ex.Explanation,
            ScheduleConflictException ex => ex.Explanation,
            _ => null
        };
        return explanation is null ? problem : WithExtension(problem, "explanation", explanation);
    }

    private static ProblemDetails MapProblem(Exception exception) => exception switch
    {
        // Never forcible, so it carries no "forcible" (SPEC 0066 R4).
        DriverUnavailableException ex => WithExtension(
            Problem(StatusCodes.Status422UnprocessableEntity, Texts.DriverUnavailableTitle(), ex.Text),
            "violations", ex.Violations),
        // Only a compliance rejection can be forced: the client reads "forcible" (SPEC 0056 R1) and the later services a forced
        // assignment would leave illegal (SPEC 0063 R7).
        ComplianceException ex => WithExtension(WithExtension(WithExtension(
            Problem(StatusCodes.Status422UnprocessableEntity, Texts.NotCompliantTitle(), ex.Text),
            "violations", ex.Violations), "forcible", true),
            "laterServices", ex.LaterServices.Select(i => new { i.TripId, i.Start, i.With.Article, Rule = i.With.Name, i.Message }).ToList()),
        VehicleIneligibleException ex => WithExtension(
            Problem(StatusCodes.Status422UnprocessableEntity, Texts.VehicleNotFitTitle(), ex.Text),
            "violations", ex.Violations),
        ScheduleConflictException ex => WithExtension(
            Problem(StatusCodes.Status409Conflict, Texts.ScheduleOverlapTitle(), ex.Text),
            "conflictingTripIds", ex.ConflictingTripIds),
        TripNotAllocatableException ex => Problem(StatusCodes.Status409Conflict, Texts.NotAllocatableTitle(), ex.Text),
        UnavailabilityFinishedException ex => Problem(StatusCodes.Status409Conflict, Texts.AgendaEntryTitle(), ex.Text),
        // Without "field": that is how the client tells it from a repeated DNI or plate (SPEC 0090 R2).
        DetailsChangedException ex => Problem(StatusCodes.Status409Conflict, Texts.DetailsChangedTitle(), ex.Text),
        ValueAlreadyUsedException ex => WithExtension(
            Problem(StatusCodes.Status409Conflict, Texts.RepeatedTitle(), ex.Text), "field", ex.Field),
        ResourceBusyException ex => WithExtension(
            Problem(StatusCodes.Status503ServiceUnavailable, Texts.BusyTitle(), ex.Text), "retryable", true),
        AttemptNotRecordedException => WithExtension(
            Problem(StatusCodes.Status503ServiceUnavailable, Texts.AttemptNotRecordedTitle(), Texts.AttemptNotRecorded()),
            "retryable", true),
        // Invariant violations and unknown ids.
        ArgumentException ex => Problem(StatusCodes.Status400BadRequest, Texts.InvalidRequestTitle(), TextOf(ex) ?? Texts.InvalidRequest()),
        KeyNotFoundException ex => Problem(StatusCodes.Status404NotFound, Texts.NotFoundTitle(), TextOf(ex)),
        // Binding failures (e.g. driverId=abc) are thrown in Development; keep their own 4xx.
        BadHttpRequestException ex => Problem(ex.StatusCode, Texts.MalformedRequestTitle(), Texts.MalformedRequest()),
        _ => Problem(StatusCodes.Status500InternalServerError, Texts.UnexpectedTitle(), detail: null)
    };

    /// <summary>Null when its message is only for the log: it names code, and in English.</summary>
    private static LocalizedText? TextOf(Exception exception) => (exception as IHasLocalizedText)?.Text;

    private static ProblemDetails Problem(int status, LocalizedText title, LocalizedText? detail) =>
        new() { Status = status, Title = title.In(CurrentLanguage.Value), Detail = detail?.In(CurrentLanguage.Value) };

    private static ProblemDetails WithExtension(ProblemDetails problem, string key, object value)
    {
        problem.Extensions[key] = value;
        return problem;
    }
}
