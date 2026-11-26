using Microsoft.Net.Http.Headers;

namespace DispatchGuard.API.Http;

/// <summary>
/// The language of the answer, from the standard <c>Accept-Language</c>: English unless the caller prefers Spanish (SPEC 0095 R2).
/// It is the outermost step, so that the errors answered by any other one are in that language too.
/// </summary>
public sealed class RequestLanguage(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var language = Preferred(context.Request);
        CurrentLanguage.Value = language;
        // Only the API has texts: the files of the web are the same for everybody, and their cache must not depend on it.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.ContentLanguage = language.Code();
                context.Response.Headers.AppendCommaSeparatedValues(HeaderNames.Vary, HeaderNames.AcceptLanguage);
                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    internal static Language Preferred(HttpRequest request)
    {
        // A malformed header says nothing.
        if (!StringWithQualityHeaderValue.TryParseList(request.Headers.AcceptLanguage, out var preferences))
        {
            return Language.English;
        }

        return preferences
            .Where(preference => preference.Quality is not 0)
            .OrderByDescending(preference => preference.Quality ?? 1)
            .Select(preference => LanguageOf(preference.Value.Value))
            .FirstOrDefault(language => language is not null) ?? Language.English;
    }

    private static Language? LanguageOf(string? tag) => tag?.Split('-')[0].ToLowerInvariant() switch
    {
        "es" => Language.Spanish,
        "en" => Language.English,
        _ => null
    };
}
