using System.Runtime.CompilerServices;

namespace DispatchGuard.Core.Localization;

/// <summary>
/// Every text the API shows: one method per text, with both languages side by side, so that none can be missing. The name of the
/// method is the code it is stored with (see <see cref="CodedTextJsonConverter"/>). Legal terms in English are the ones of the
/// official text of the Regulations (SPEC 0095 R4).
/// </summary>
public static partial class Texts
{
    /// <summary>Written by a person, or recorded before texts had a code: the same in both languages.</summary>
    public static LocalizedText AsWritten(string text) => Of("{0}", "{0}", [text]);

    /// <summary>Each reason on its own, as they are listed to the dispatcher.</summary>
    public static LocalizedText Listed(IReadOnlyList<LocalizedText> texts) => Of("{0:; }", "{0:; }", [texts]);

    public static LocalizedText Dotted(IReadOnlyList<LocalizedText> texts) => Of("{0: · }", "{0: · }", [texts]);

    public static LocalizedText Sentences(IReadOnlyList<LocalizedText> texts) => Of("{0:. }", "{0:. }", [texts]);

    public static LocalizedText Titled(LocalizedText title, LocalizedText detail) => Of("{0}: {1}", "{0}: {1}", [title, detail]);

    public static LocalizedText Sentence(LocalizedText text) => Of("{0}.", "{0}.", [text]);

    private static LocalizedText Of(string english, string spanish, object?[]? arguments = null, [CallerMemberName] string key = "") =>
        new(key, english, spanish, arguments ?? []);
}
