namespace DispatchGuard.Tests;

/// <summary>Most tests read the Spanish text, the one there was before the catalogue: that is how they prove it did not change.</summary>
internal static class SpanishText
{
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(text))]
    public static string? Es(this LocalizedText? text) => text?.In(Language.Spanish);

    public static IEnumerable<string> Es(this IEnumerable<LocalizedText> texts) => texts.Select(text => text.Es());
}
