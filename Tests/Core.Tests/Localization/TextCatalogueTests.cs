using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Localization;

/// <summary>The catalogue of the API (SPEC 0095 CA1, CA2, CA4).</summary>
public class TextCatalogueTests
{
    [Fact]
    public void EveryText_HasBothLanguages_WithTheSameParameters() => Assert.All(TextCatalogue.All, AssertSameParameters);

    private static void AssertSameParameters(MethodInfo method)
    {
        var key = method.Name;
        var text = Sample(method);
        var english = Placeholders(text, Language.English);
        var spanish = Placeholders(text, Language.Spanish);

        Assert.Equal(key, text.Key);
        Assert.Equal(method.GetParameters().Length, text.Arguments.Count);
        Assert.Equal(Enumerable.Range(0, text.Arguments.Count), english.Select(p => p.Index).Distinct().Order());
        Assert.Equal(english.Select(p => p.Index).Distinct().Order(), spanish.Select(p => p.Index).Distinct().Order());
        // The same format in both, so that a figure never loses a decimal in one of them. A list only differs in its quotes.
        Assert.Equal(
            english.Where(p => !IsList(text, p.Index) && p.Format.Length > 0).Order(),
            spanish.Where(p => !IsList(text, p.Index) && p.Format.Length > 0).Order());
        Assert.Equal(key == nameof(Texts.NoPlace), string.IsNullOrWhiteSpace(text.In(Language.English)));
        Assert.Equal(key == nameof(Texts.NoPlace), string.IsNullOrWhiteSpace(text.In(Language.Spanish)));
    }

    [Fact]
    public void EveryText_StoredAsACode_ComesBackTheSame()
    {
        var options = new JsonSerializerOptions { Converters = { new CodedTextJsonConverter() } };

        Assert.All(TextCatalogue.All.Select(Sample),
            text => Assert.Equal(text, JsonSerializer.Deserialize<LocalizedText>(JsonSerializer.Serialize(text, options), options)));
    }

    [Theory]
    [InlineData("""{"k":"NoSuchText","a":[]}""")]
    [InlineData("""{"k":"NotEnoughSeats","a":[1]}""")]
    [InlineData("""{"a":[]}""")]
    [InlineData("\"text\"")]
    public void Code_Unknown_IsUnreadable(string stored)
    {
        var options = new JsonSerializerOptions { Converters = { new CodedTextJsonConverter() } };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LocalizedText>(stored, options));
    }

    [Theory]
    [InlineData(0, "0 tasks left to solve", "Quedan 0 tareas por resolver")]
    [InlineData(1, "1 task left to solve", "Queda 1 tarea por resolver")]
    [InlineData(2, "2 tasks left to solve", "Quedan 2 tareas por resolver")]
    public void Plural_FollowsTheNumber(int tasks, string english, string spanish)
    {
        Assert.Equal(english, Texts.TasksLeft(tasks).In(Language.English));
        Assert.Equal(spanish, Texts.TasksLeft(tasks).In(Language.Spanish));
    }

    [Fact]
    public void Language_IsOfTheRequest_NotOfTheThread()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("\"Saved\"", JsonSerializer.Serialize(Texts.Saved()));
            CurrentLanguage.Value = Language.Spanish;
            Assert.Equal("\"Guardado\"", JsonSerializer.Serialize(Texts.Saved()));
        }
        finally
        {
            CurrentLanguage.Value = Language.English;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        }
    }

    /// <summary>
    /// The only legal difference between the two texts is the language: figures, articles and dates are the same (SPEC 0095 R3).
    /// </summary>
    [Fact]
    public void Explanations_EveryFigure_IsTheSameInBothLanguages()
    {
        var texts = Explanations().SelectMany(TextsOf).ToList();

        Assert.True(texts.Count > 100, $"Only {texts.Count} texts were compared");
        Assert.All(texts, text => Assert.Equal(Figures(text, Language.Spanish), Figures(text, Language.English)));
    }

    [Fact]
    public void Rules_EveryFigure_IsTheSameInBothLanguages() =>
        Assert.All(RulesCatalog.Build().SelectMany(group => group.Rules).SelectMany(rule => new[] { rule.Name, rule.Explanation, rule.Figure }),
            text => Assert.Equal(Figures(text, Language.Spanish), Figures(text, Language.English)));

    [Fact]
    public void Attempt_KeepsTheSpanishWordsThatThePreviousVersionReads()
    {
        var explanation = Explanations().First();
        using var stored = JsonDocument.Parse(AttemptTexts.Serialize(explanation, explanation.Summary));

        Assert.Equal(explanation.Summary.In(Language.Spanish), stored.RootElement.GetProperty("Summary").GetString());
        Assert.Equal(explanation.Business[0].Title.In(Language.Spanish), stored.RootElement.GetProperty("Business")[0].GetProperty("Title").GetString());
        Assert.Equal(JsonValueKind.String, stored.RootElement.GetProperty("Flow")[0].GetProperty("Role").ValueKind);
        Assert.Equal(JsonValueKind.String, stored.RootElement.GetProperty("Patterns")[0].ValueKind);
    }

    [Theory]
    [InlineData("""{"Detail":{"k":"NoSuchText"},"Explanation":null}""")]
    [InlineData("[]")]
    [InlineData("3")]
    public void Attempt_WithCodesOfAnotherVersion_IsReadAsItWasWritten(string codes)
    {
        var json = $$"""{"Summary":"Resumen","Business":[],"Flow":[],"Patterns":[],"Adrs":[],"Coded":{{codes}}}""";
        var attempt = new AllocationAttempt(DateTime.UtcNow, "Ana", 1, 1, 1, 1, AllocationOutcome.RejectedDriver, "Detalle", explanation: json);

        var texts = AttemptTexts.Of(attempt);

        Assert.Equal("Detalle", texts.Detail!.In(Language.English));
        Assert.Equal("Resumen", texts.Explanation!.Summary.In(Language.English));
    }

    private static readonly DateTime Day = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Broken and met rules, with later services, a forced allocation, a proposal and an unavailable driver.</summary>
    private static IEnumerable<DecisionExplanation> Explanations()
    {
        var window = new TimeWindow(Day.AddDays(7).AddHours(19.25), Day.AddDays(7).AddHours(25.25));
        var driver = new Driver("1", "Ana", "Gil");
        var available = DriverEligibilityCheck.For(driver, window);
        var tired = Enumerable.Range(0, 8).Select(d => new DrivingPeriod(1, Day.AddDays(d).AddHours(4), Day.AddDays(d).AddHours(19))).ToArray();
        var later = new[] { new DrivingPeriod(1, Day.AddHours(13), Day.AddHours(18), 20) };
        var broken = LegalComplianceEvaluation.Validate(tired, window.Start, TimeSpan.FromHours(6));
        var met = LegalComplianceEvaluation.Validate([new DrivingPeriod(1, Day.AddHours(6), Day.AddHours(10))], Day.AddHours(11), TimeSpan.FromHours(2));
        var brokenLater = LegalComplianceEvaluation.Validate(later, Day.AddHours(10), TimeSpan.FromHours(3))
            .WithLaterServices(LaterServicesEvaluation.Evaluate(later, new DrivingPeriod(1, Day.AddHours(10), Day.AddHours(13), 10)));
        Assert.True(brokenLater.LaterServices!.HasImpacts);

        yield return DecisionExplainer.ForAllocation(Trace(available, broken));
        yield return DecisionExplainer.ForAllocation(Trace(available, met));
        yield return DecisionExplainer.ForAllocation(Trace(available, brokenLater));
        yield return DecisionExplainer.ForProposal(Trace(available, met));
        yield return DecisionExplainer.ForProposal(Trace(available, broken));
        yield return DecisionExplainer.ForForcedAllocation(Trace(available, broken), AllocationOverride.Create("No hay otro conductor hoy", "Lucía"));
        driver.AddUnavailability(new DriverUnavailability(1, window, UnavailabilityType.SickLeave));
        yield return DecisionExplainer.ForAllocation(Trace(DriverEligibilityCheck.For(driver, window), null));
    }

    private static AllocationTrace Trace(DriverEligibilityCheck driver, ComplianceCheckResult? compliance) =>
        new(driver, compliance, VehicleEligibilityCheck.Eligible(), ScheduleConflictCheck.NoConflict(), ScheduleConflictCheck.Conflict([7, 8]), 40, 55);

    private static IEnumerable<LocalizedText> TextsOf(DecisionExplanation explanation) =>
        explanation.Business.SelectMany(item => new[] { item.Title, item.Detail })
            .Concat(explanation.Flow.SelectMany(step => new[] { step.Role, step.Result }))
            .Concat(explanation.Patterns)
            .Append(explanation.Summary);

    private static IEnumerable<string> Figures(LocalizedText text, Language language) =>
        Regex.Matches(text.In(language).Replace(',', '.'), @"\d+(\.\d+)?").Select(match => match.Value).Order(StringComparer.Ordinal);

    private sealed record Placeholder(int Index, string Format) : IComparable<Placeholder>
    {
        public int CompareTo(Placeholder? other) => string.CompareOrdinal($"{Index}:{Format}", $"{other?.Index}:{other?.Format}");
    }

    private static List<Placeholder> Placeholders(LocalizedText text, Language language) =>
        LocalizedText.Placeholder().Matches(text.TemplateIn(language))
            // {1} of the annotations is written by them, not by the catalogue.
            .Where(_ => text.Arguments.Count > 0)
            .Select(match => new Placeholder(
                int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture), match.Groups["format"].Value))
            .ToList();

    private static bool IsList(LocalizedText text, int index) => text.Arguments[index] is IEnumerable<LocalizedText>;

    private static LocalizedText Sample(MethodInfo method) =>
        (LocalizedText)method.Invoke(null, method.GetParameters().Select((parameter, i) => SampleOf(parameter.ParameterType, i + 2)).ToArray())!;

    private static object SampleOf(Type type, int number) => type switch
    {
        _ when type == typeof(int) => number,
        _ when type == typeof(double) => number + 0.5,
        _ when type == typeof(string) => $"text {number}",
        _ when type == typeof(DateTime) => new DateTime(2026, 11, number, 8, 30, 0, DateTimeKind.Utc),
        _ when type == typeof(LocalizedText) => Texts.NotEnoughSeats(number, number + 1),
        _ when type == typeof(IReadOnlyList<LocalizedText>) => new List<LocalizedText> { Texts.Saved(), Texts.Complies() },
        _ when type == typeof(IReadOnlyList<double>) => new List<double> { number, number + 0.5 },
        _ => throw new NotSupportedException($"A stored text cannot have a {type}: give it a sample here and check that it comes back")
    };
}
