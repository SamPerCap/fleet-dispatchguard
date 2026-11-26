using DispatchGuard.API.Auditing;
using DispatchGuard.API.Session;
using DispatchGuard.API.XRay;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DispatchGuard.API.Endpoints;

public static class XRayEndpoints
{
    public static void MapXRayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/xray", GetXRay).WithName("XRay").WithTags("XRay");
    }

    /// <summary>The token goes in a header and never in the address, which servers and browsers keep (SPEC 0097 R4).</summary>
    [NotTraced]
    private static Results<Ok<XRayDto>, NotFound> GetXRay(
        HttpContext context, XRayTokens tokens, [FromHeader(Name = XRayTokens.Header)] string? token)
    {
        context.Response.Headers.CacheControl = "no-store";
        return AnonymousSession.IdOf(context.User) is { } session && tokens.Read(token, session) is { } xray
            ? TypedResults.Ok(xray)
            : TypedResults.NotFound();
    }
}
