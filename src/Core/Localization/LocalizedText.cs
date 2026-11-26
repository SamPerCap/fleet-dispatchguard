using System.Collections;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Localization;

/// <summary>
/// A text of the catalogue (<see cref="Texts"/>) with its arguments, worded only when it is read: the same one can be answered in
/// either language and stored as a code (SPEC 0095).
/// </summary>
/// <remarks>
/// Templates admit <c>{0}</c>, <c>{0:format}</c> (instants are shown in Madrid time), <c>{0:lower}</c> for a text in the middle of a
/// sentence and <c>{0|singular|plural}</c>.
/// </remarks>
[JsonConverter(typeof(LocalizedTextJsonConverter))]
public sealed partial class LocalizedText : IEquatable<LocalizedText>
{
    private const string LowerCase = "lower";

    private readonly string _english;
    private readonly string _spanish;

    internal LocalizedText(string key, string english, string spanish, IReadOnlyList<object?> arguments)
    {
        Key = key;
        _english = english;
        _spanish = spanish;
        Arguments = arguments;
    }

    /// <summary>Name of the method of <see cref="Texts"/> that made it.</summary>
    public string Key { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public string TemplateIn(Language language) => language == Language.Spanish ? _spanish : _english;

    public string In(Language language)
    {
        var template = TemplateIn(language);
        return Arguments.Count == 0 ? template : Placeholder().Replace(template, match =>
        {
            var argument = Arguments[int.Parse(match.Groups["index"].ValueSpan, CultureInfo.InvariantCulture)];
            return match.Groups["one"].Success
                ? match.Groups[IsOne(argument) ? "one" : "many"].Value
                : Worded(argument, match.Groups["format"].Value, language);
        });
    }

    /// <summary>For the logs.</summary>
    public override string ToString() => In(Language.English);

    public bool Equals(LocalizedText? other) =>
        other is not null && Key == other.Key && In(Language.English) == other.In(Language.English) && In(Language.Spanish) == other.In(Language.Spanish);

    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    public override int GetHashCode() => HashCode.Combine(Key, In(Language.English));

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static bool IsOne(object? argument) => Convert.ToDouble(argument, CultureInfo.InvariantCulture) == 1;

    private static string Worded(object? argument, string format, Language language) => argument switch
    {
        null => string.Empty,
        LocalizedText text => format == LowerCase ? LowerFirst(text.In(language)) : text.In(language),
        string text => text,
        DateTime instant => DisplayFormat.Madrid(instant, format, language.Culture()),
        // The format of a list is its separator.
        IEnumerable list => string.Join(format.Length > 0 ? format : ", ", list.Cast<object?>().Select(item => Worded(item, string.Empty, language))),
        IFormattable value => value.ToString(format, language.Culture()),
        _ => argument.ToString() ?? string.Empty
    };

    [GeneratedRegex(@"\{(?<index>\d+)(?:\|(?<one>[^|}]*)\|(?<many>[^}]*)|:(?<format>[^}]*))?\}", RegexOptions.ExplicitCapture)]
    public static partial Regex Placeholder();
}
