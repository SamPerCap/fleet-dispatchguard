using DispatchGuard.API.Http;
using Microsoft.AspNetCore.Mvc;

namespace DispatchGuard.API.Session;

/// <summary>Marks an endpoint that answers without a session: it gives one, or checks a key of its own.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WithoutSessionAttribute : Attribute;

/// <summary>
/// Nothing is read or written without a session: the database sleeps, and whoever has none must not wake it up (SPEC 0086). To
/// write, the header is the second barrier against a forged request, after <c>SameSite=Strict</c> (SPEC 0084): another site can make
/// the browser send a form, but not a header of its own.
/// </summary>
public sealed class SessionGuard(RequestDelegate next, SessionSettings settings, IProblemDetailsService problems)
{
    public const string Header = "X-DispatchGuard-Request";

    /// <summary>The web reads it to tell this 401 from a wrong key.</summary>
    private const string SessionMissing = "sessionMissing";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!settings.IsRequired || !IsGuarded(context))
        {
            await next(context);
        }
        else if (!context.Request.IsRead() && !CarriesTheHeader(context.Request))
        {
            await RejectAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden, Title = Texts.ForbiddenTitle().In(CurrentLanguage.Value), Detail = Texts.HeaderMissing(Header).In(CurrentLanguage.Value)
            });
        }
        else if (AnonymousSession.IdOf(context.User) is null)
        {
            await RejectAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized, Title = Texts.SessionMissingTitle().In(CurrentLanguage.Value),
                Extensions = { [SessionMissing] = true }
            });
        }
        else
        {
            await next(context);
        }
    }

    private static bool CarriesTheHeader(HttpRequest request) =>
        request.Headers.ContainsKey(Header);

    /// <summary>Every endpoint unless it says otherwise: a new one is closed without anyone having to remember it.</summary>
    private static bool IsGuarded(HttpContext context) =>
        context.GetEndpoint() is { } endpoint && endpoint.Metadata.GetMetadata<WithoutSessionAttribute>() is null;

    private async Task RejectAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status!.Value;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
