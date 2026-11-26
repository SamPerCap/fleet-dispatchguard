namespace DispatchGuard.API.Errors;

/// <summary>
/// The framework answers in English, without <c>detail</c> for a validation failure and with titles of its own for an empty status,
/// but the web shows <c>title</c> and <c>detail</c> to the dispatcher as they come.
/// </summary>
public static class ProblemTexts
{
    public static void Customize(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is HttpValidationProblemDetails validation)
        {
            validation.Title = Texts.InvalidRequestTitle().In(CurrentLanguage.Value);
            validation.Detail = string.Join(" · ", validation.Errors.Values.SelectMany(messages => messages));
        }
        else if (context.Exception is null && IsFromTheFramework(context.ProblemDetails.Title) && TitleOf(context.ProblemDetails.Status) is { } title)
        {
            context.ProblemDetails.Title = title.In(CurrentLanguage.Value);
        }
    }

    private static bool IsFromTheFramework(string? title) => title is null || FrameworkTitles.Contains(title);

    /// <summary>Those of the statuses it answers on its own (<c>UseStatusCodePages</c>).</summary>
    private static readonly HashSet<string> FrameworkTitles =
    [
        "Bad Request", "Unauthorized", "Forbidden", "Not Found", "Method Not Allowed", "Content Too Large", "Unsupported Media Type",
        "An error occurred while processing your request.", "Service Unavailable"
    ];

    private static LocalizedText? TitleOf(int? status) => status switch
    {
        StatusCodes.Status400BadRequest => Texts.MalformedRequestTitle(),
        StatusCodes.Status401Unauthorized => Texts.UnauthorizedTitle(),
        StatusCodes.Status403Forbidden => Texts.ForbiddenTitle(),
        StatusCodes.Status404NotFound => Texts.NotFoundTitle(),
        StatusCodes.Status405MethodNotAllowed => Texts.MethodNotAllowedTitle(),
        StatusCodes.Status413PayloadTooLarge => Texts.TooLargeTitle(),
        StatusCodes.Status415UnsupportedMediaType => Texts.UnsupportedMediaTitle(),
        StatusCodes.Status429TooManyRequests => Texts.TooManyRequestsTitle(),
        StatusCodes.Status500InternalServerError => Texts.UnexpectedTitle(),
        StatusCodes.Status503ServiceUnavailable => Texts.UnavailableTitle(),
        _ => null
    };
}
