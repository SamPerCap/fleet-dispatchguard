using DispatchGuard.API.Demo;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.DemoData;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/demo/reset", ResetDemoData).ExcludeFromDescription();
        // A GET, although it writes: it is the only thing the scheduler of Vercel knows how to send.
        app.MapGet("/api/admin/demo/rebuild", RebuildDemo).ExcludeFromDescription();
    }

    [WithoutSession]
    private static async Task<Results<NoContent, ProblemHttpResult>> ResetDemoData(
        HttpRequest request, DemoResetKey resetKey, IDemoDataReset demoData, CancellationToken cancellationToken)
    {
        if (!resetKey.IsConfigured)
        {
            return NotADemo();
        }

        if (!resetKey.IsIn(request))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Texts.ResetKeyTitle().In(CurrentLanguage.Value));
        }

        await demoData.ResetAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>What needs the database is resolved at the end: whoever lacks the secret must not wake it up.</summary>
    [WithoutSession]
    private static async Task<Results<Ok<SeedReport>, ProblemHttpResult>> RebuildDemo(
        HttpContext context, CronSecret secret, CancellationToken cancellationToken)
    {
        if (!secret.IsConfigured)
        {
            return NotADemo();
        }

        if (!secret.IsIn(context.Request))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Texts.ResetKeyTitle().In(CurrentLanguage.Value));
        }

        var rebuild = context.RequestServices.GetRequiredService<DemoRebuild>();
        return await rebuild.RunAsync(context.Request, cancellationToken) is { } report
            ? TypedResults.Ok(report)
            : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Texts.AlreadyRebuildingTitle().In(CurrentLanguage.Value));
    }

    /// <summary>404 and not 401: without a key this is not a demo installation and must not reveal that the endpoint exists (ADR 0014).</summary>
    private static ProblemHttpResult NotADemo() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: Texts.NotFoundTitle().In(CurrentLanguage.Value));
}
