using System.Globalization;

namespace DispatchGuard.Core.Localization;

public enum Language
{
    English = 0,
    Spanish = 1
}

public static class Languages
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static CultureInfo Culture(this Language language) => language == Language.Spanish ? Spanish : English;

    /// <summary>As in <c>Content-Language</c>.</summary>
    public static string Code(this Language language) => language == Language.Spanish ? "es" : "en";
}

/// <summary>
/// Language of the request being answered. Only who writes the answer reads it: rules never do (SPEC 0095 R3). It is not the culture
/// of the thread, which also decides how numbers are read.
/// </summary>
public static class CurrentLanguage
{
    private static readonly AsyncLocal<Language> Current = new();

    public static Language Value
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
