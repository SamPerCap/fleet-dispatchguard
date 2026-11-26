using DispatchGuard.API.Auditing;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public sealed record SessionDto(string ShortId);

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/session", (Delegate)GetOrStartSession).WithName("Session").WithTags("Session");
    }

    [StartsSession]
    [WithoutSession]
    [NotTraced]
    private static async Task<Ok<SessionDto>> GetOrStartSession(HttpContext context)
    {
        // It carries a cookie: no cache may hand it to another browser.
        context.Response.Headers.CacheControl = "no-store";
        var id = AnonymousSession.IdOf(context.User) ?? await AnonymousSession.StartAsync(context);
        return TypedResults.Ok(new SessionDto(AnonymousSession.ShortIdOf(id)));
    }
}
