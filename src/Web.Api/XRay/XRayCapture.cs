using System.Diagnostics;
using DispatchGuard.API.Session;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Localization;

namespace DispatchGuard.API.XRay;

/// <summary>Marks an endpoint whose answer can be looked inside by whoever asked for it (SPEC 0097).</summary>
/// <param name="kind">What the web calls the answer: one of <see cref="XRayKinds"/>.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class XRayedAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}

public static class XRayKinds
{
    public const string Allocation = "allocation";
    public const string Recommendation = "recommendation";
}

/// <summary>
/// Seals what the operation did into a header of its own answer, as the answer starts: by then the engine has decided, the
/// transaction has ended and an exception has already become its problem.
/// </summary>
public sealed class XRayCapture(RequestDelegate next, ILogger<XRayCapture> logger)
{
    public async Task InvokeAsync(HttpContext context, XRayTokens tokens)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<XRayedAttribute>() is not { } mark
            || OperationTrace.Current is not { } operation
            || AnonymousSession.IdOf(context.User) is not { } session)
        {
            await next(context);
            return;
        }

        var clock = Stopwatch.StartNew();
        var language = CurrentLanguage.Value;
        context.Response.OnStarting(() =>
        {
            // The answer may already be saved: losing its X-ray is better than turning it into a failure.
            try
            {
                var xray = XRayReport.Of(mark.Kind, context.Response.StatusCode, clock.Elapsed, operation, language);
                context.Response.Headers[XRayTokens.Header] = tokens.Issue(session, xray);
            }
            catch (Exception exception)
            {
                logger.LogWarning("The X-ray of {Kind} was lost: {Reason}", mark.Kind, exception.GetType().Name);
            }

            return Task.CompletedTask;
        });
        await next(context);
    }
}
