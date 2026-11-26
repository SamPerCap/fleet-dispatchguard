namespace DispatchGuard.API.Perimeter;

/// <summary>Headers of every answer. Written when it starts: the exception handler wipes whatever was written before it.</summary>
public sealed class SecurityHeaders(RequestDelegate next, IHostEnvironment environment)
{
    private const string Contained = "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>Nothing the API answers is a page that loads, sends or frames anything.</summary>
    private const string ClosedPolicy = $"default-src 'none'; {Contained}";

    /// <summary>Scripts are never inline. Styles are: the dialog library inserts one, with the width of the scrollbar it hides.</summary>
    private const string PagePolicy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self'; connect-src 'self'; " +
        Contained;

    private const string NoPermissions =
        "accelerometer=(), browsing-topics=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), " +
        "microphone=(), midi=(), payment=(), usb=()";

    private const string OneYear = "max-age=31536000";

    /// <summary>A browser would keep asking localhost for HTTPS, also for other projects.</summary>
    private readonly bool _requiresHttps = !environment.IsDevelopmentOrTesting();

    private readonly bool _servesSwagger = environment.IsDevelopment();

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            Write(context);
            return Task.CompletedTask;
        });
        return next(context);
    }

    private void Write(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = NoPermissions;

        if (_requiresHttps && context.Request.IsHttps)
        {
            headers.StrictTransportSecurity = OneYear;
        }

        // Swagger is a page with its own scripts; everything else in Development has the policy of Production.
        var isApi = context.Request.Path.StartsWithSegments("/api");
        if (!_servesSwagger || isApi)
        {
            headers.ContentSecurityPolicy = !isApi && IsPage(context.Response) ? PagePolicy : ClosedPolicy;
        }
    }

    private static bool IsPage(HttpResponse response) =>
        response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
