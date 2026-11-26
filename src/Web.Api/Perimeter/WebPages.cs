namespace DispatchGuard.API.Perimeter;

/// <summary>The web, from the same origin as the API: no CORS, and the cookie of the session never leaves it (SPEC 0091).</summary>
public static class WebPages
{
    private const string Index = "/index.html";

    /// <summary>Vite names these files after their content: another content is another file.</summary>
    private const string HashedFiles = "/assets";

    private const string OneYear = "public, max-age=31536000, immutable";

    /// <summary>It may be kept, but not used without asking if it is still the one.</summary>
    private const string AlwaysAsk = "no-cache";

    /// <summary>In development there is no web here: Vite serves it.</summary>
    public static void UseWebPages(this WebApplication app)
    {
        if (!app.Environment.WebRootFileProvider.GetFileInfo(Index).Exists)
        {
            return;
        }

        app.Use((context, next) =>
        {
            if (IsPage(context))
            {
                context.Request.Path = Index;
            }

            return next(context);
        });
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = file => SetCache(file.Context) });
    }

    /// <summary>
    /// The web decides what its paths mean. One with an extension is a file, and a missing file is not found: after a deployment,
    /// an open page that asks for the script of the previous version must not get a page.
    /// </summary>
    private static bool IsPage(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.GetEndpoint() is null
        && !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/health")
        && !Path.HasExtension(context.Request.Path);

    private static void SetCache(HttpContext context) =>
        context.Response.Headers.CacheControl = context.Request.Path.StartsWithSegments(HashedFiles) ? OneYear : AlwaysAsk;
}
