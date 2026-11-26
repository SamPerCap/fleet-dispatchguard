using System.Text.Json;
using System.Text.Json.Serialization;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Engine;

/// <summary>
/// The texts of an attempt as they are stored. The explanation is stored twice: worded in <see cref="StoredLanguage"/>, which is all
/// that older attempts and the previous version know, and as codes, which are worded in the language of whoever reads them
/// (SPEC 0095 R5). What cannot be read never hides the attempt (SPEC 0059).
/// </summary>
public sealed record AttemptTexts(LocalizedText? Detail, DecisionExplanation? Explanation)
{
    /// <summary>The one every attempt had before SPEC 0095.</summary>
    public const Language StoredLanguage = Language.Spanish;

    /// <summary>Enums as names, so that the stored text stays readable.</summary>
    private static readonly JsonSerializerOptions Worded = new()
    {
        Converters = { new JsonStringEnumConverter(), new WordedTextJsonConverter(StoredLanguage) }
    };

    private static readonly JsonSerializerOptions Coded = new()
    {
        Converters = { new JsonStringEnumConverter(), new CodedTextJsonConverter() }
    };

    private sealed record CodedTexts(LocalizedText? Detail, DecisionExplanation Explanation);

    /// <summary>The previous version reads the explanation and ignores <c>Coded</c>.</summary>
    private sealed record StoredTexts(
        LocalizedText Summary, IReadOnlyList<ExplanationItem> Business, IReadOnlyList<TechnicalStep> Flow, IReadOnlyList<LocalizedText> Patterns,
        IReadOnlyList<string> Adrs, JsonElement? Coded);

    public static string Serialize(DecisionExplanation explanation, LocalizedText? detail)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        return JsonSerializer.Serialize(
            new StoredTexts(explanation.Summary, explanation.Business, explanation.Flow, explanation.Patterns, explanation.Adrs,
                JsonSerializer.SerializeToElement(new CodedTexts(detail, explanation), Coded)),
            Worded);
    }

    public static AttemptTexts Of(AllocationAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var detail = attempt.Detail is { } recorded ? Texts.AsWritten(recorded) : null;
        if (Read<StoredTexts>(attempt.Explanation, Worded) is not { Summary: not null } stored)
        {
            return new AttemptTexts(detail, null);
        }

        return stored.Coded is { } codes && Read<CodedTexts>(codes.GetRawText(), Coded) is { Explanation: not null } coded
            ? new AttemptTexts(coded.Detail ?? detail, coded.Explanation)
            : new AttemptTexts(detail, new DecisionExplanation(stored.Summary, stored.Business, stored.Flow, stored.Patterns, stored.Adrs));
    }

    private static TTexts? Read<TTexts>(string? json, JsonSerializerOptions options) where TTexts : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TTexts>(json, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
