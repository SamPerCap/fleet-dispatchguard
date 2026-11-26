using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(2026, 11, 22, 23, 30, "23/11 00:30")] // winter, UTC+1: next day in Madrid
    [InlineData(2026, 7, 1, 6, 0, "01/07 08:00")] // summer, UTC+2
    [InlineData(2026, 10, 25, 0, 59, "25/10 02:59")] // last summer-time minute
    [InlineData(2026, 10, 25, 1, 0, "25/10 02:00")] // clocks went back
    public void Madrid_UtcInstant_IsShownInMadridTime(int year, int mo, int day, int hour, int mi, string expected) =>
        Assert.Equal(expected, DisplayFormat.Madrid(new DateTime(year, mo, day, hour, mi, 0), "dd/MM HH:mm", Language.Spanish.Culture()));

    [Fact]
    public void Number_AnyServerCulture_IsWrittenAsInItsLanguage()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal("7,4 km", Texts.Kilometres(7.42).In(Language.Spanish));
            Assert.Equal("7.4 km", Texts.Kilometres(7.42).In(Language.English));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Validate_DailyRestNote_IsInMadridTimeWithoutUtc()
    {
        var day = new DateTime(2026, 11, 16);
        var result = LegalComplianceEvaluation.Validate([new DrivingPeriod(1, day.AddHours(6), day.AddHours(10))], day.AddHours(11), TimeSpan.FromHours(2));

        var note = Assert.Single(result.RuleChecks, c => c.Article == "Art. 8.2").Note;
        Assert.Contains("16/11 07:00–16/11 14:00", note.Es()); // 06:00Z–13:00Z
        Assert.DoesNotContain(result.RuleChecks, c => c.Note?.Es().Contains("UTC") == true);
    }

    [Fact]
    public void Validate_EveryViolation_IsInSpanish()
    {
        // Eight 15 h days in a row: breaks continuous, daily, weekly and fortnight driving, and both rests.
        var day = new DateTime(2026, 11, 2);
        var history = Enumerable.Range(0, 8).Select(d => new DrivingPeriod(1, day.AddDays(d).AddHours(4), day.AddDays(d).AddHours(19))).ToArray();

        var messages = LegalComplianceEvaluation.Validate(history, day.AddDays(7).AddHours(19.25), TimeSpan.FromHours(6))
            .Violations.Select(v => v.Message).ToList();

        Assert.Contains(messages, m => m.Es().StartsWith("Conducción continua: 21 h (máximo 4,5 h)", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Es().StartsWith("Conducción diaria:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Es().StartsWith("Conducción en dos semanas:", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Es().StartsWith("Jornada ", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Es().StartsWith("Descanso semanal pendiente: ninguno (45 h, o 24 h reducido)", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Es().Contains("would be") || m.Es().Contains("maximum") || m.Es().Contains("rest "));
    }
}
