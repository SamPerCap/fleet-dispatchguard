using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DispatchGuard.API.Http;
using DispatchGuard.API.Session;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using Microsoft.AspNetCore.Diagnostics;

namespace DispatchGuard.API.Auditing;

/// <summary>Marks an endpoint whose requests are not part of the timeline: it records it or shows it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NotTracedAttribute : Attribute;

/// <summary>
/// Records what each request did (SPEC 0085 R6, ADR 0026), before its answer ends: the host may stop the process as soon as nobody is
/// waiting. Only if it already used the database, so that it never wakes it up, and never at the cost of the request.
/// </summary>
public sealed partial class RequestTracing(RequestDelegate next, IServiceScopeFactory scopes, ILogger<RequestTracing> logger)
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(1);

    public async Task InvokeAsync(HttpContext context, IAuditOrigin origin)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || origin.IsDemoLoad)
        {
            await next(context);
            return;
        }

        var operation = OperationTrace.Current = new OperationTrace();
        var clock = Stopwatch.StartNew();
        await next(context);
        OperationTrace.Current = null;

        if (AnonymousSession.IdOf(context.User) is { } session && DeservesATrace(context, operation))
        {
            await RecordAsync(TraceOf(context, session, operation, clock.Elapsed));
        }
    }

    /// <summary>The refreshes the web makes by itself only count when they fail.</summary>
    private static bool DeservesATrace(HttpContext context, OperationTrace operation) =>
        operation.QueryCount > 0
        && EndpointOf(context).Endpoint is { } endpoint
        && endpoint.Metadata.GetMetadata<NotTracedAttribute>() is null
        && (RequestThread.Of(context).FollowsAnAction || !context.Request.IsRead() || context.Response.StatusCode >= StatusCodes.Status400BadRequest);

    private static RequestTrace TraceOf(HttpContext context, string session, OperationTrace operation, TimeSpan duration) => new()
    {
        SessionId = session,
        TraceId = RequestThread.Of(context).TraceId,
        Method = context.Request.Method,
        Path = PathOf(context),
        Status = context.Response.StatusCode,
        DurationMs = (int)duration.TotalMilliseconds,
        Queries = operation.QueryCount,
        QueriesMs = (int)operation.QueryMilliseconds,
        Detail = RequestTraceDetail.Of(operation)
    };

    /// <summary>The pattern of the endpoint with its numbers: any other part of the path could be something typed.</summary>
    private static string PathOf(HttpContext context)
    {
        var (endpoint, values) = EndpointOf(context);
        var pattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText?.TrimEnd('/') ?? string.Empty;
        var path = RouteParameter().Replace(pattern, parameter =>
            long.TryParse(values?[parameter.Groups["name"].Value]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : $"{{{parameter.Groups["name"].Value}}}");
        return path[..Math.Min(path.Length, RequestTrace.PathMaxLength)];
    }

    /// <summary>An exception erases them from the request.</summary>
    private static (Endpoint? Endpoint, RouteValueDictionary? Values) EndpointOf(HttpContext context) =>
        context.Features.Get<IExceptionHandlerFeature>() is { } failure
            ? (failure.Endpoint, failure.RouteValues)
            : (context.GetEndpoint(), context.Request.RouteValues);

    /// <summary>Apart from the request: its own context may have failed.</summary>
    private async Task RecordAsync(RequestTrace trace)
    {
        try
        {
            using var limit = new CancellationTokenSource(LongestWait);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IVisitLog>().RecordAsync(trace, limit.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning("The trace of {Method} {Path} was lost: {Reason}", trace.Method, trace.Path, exception.GetType().Name);
        }
    }

    [GeneratedRegex(@"\{(?<name>\w+)[^}]*\}")]
    private static partial Regex RouteParameter();
}

/// <summary>What a trace keeps of an operation, within its size.</summary>
public static partial class RequestTraceDetail
{
    private const int LongestQuery = 600;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Of(OperationTrace operation)
    {
        var rules = operation.Rules;
        var queries = operation.Queries.Select(Shortened).ToList();
        string detail;
        while ((detail = JsonSerializer.Serialize(new { Rules = rules, Queries = queries }, Json)).Length > RequestTrace.DetailMaxLength)
        {
            queries.RemoveAt(queries.Count - 1);
        }

        return detail;
    }

    private static TracedQuery Shortened(TracedQuery query)
    {
        var sql = Blanks().Replace(query.Sql, " ").Trim();
        return query with
        {
            Sql = sql.Length <= LongestQuery ? sql : $"{sql[..LongestQuery]}…",
            Milliseconds = Math.Round(query.Milliseconds, 1)
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Blanks();
}
