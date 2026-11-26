using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// Weekly rest not verifiable with a short history (CE 561/2006 Art. 8.6, SPEC 0040). With no rest found: if the known history
/// covers the 6×24 h before the service → critical (R2); if it does not → a <c>CE_561_2006.WeeklyRest.Unverifiable</c> warning,
/// which does not block (R3).
/// </summary>
[Trait("Regulation", "Art. 8")]
public class WeeklyRestUnverifiableTests
{
    private const string UnverifiableRuleId = "CE_561_2006.WeeklyRest.Unverifiable";
    private const string WeeklyRestRuleId = "CE_561_2006.WeeklyRest.45hours";
    private static readonly DateTime TripStart = new(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TripDuration = TimeSpan.FromHours(1);

    /// <summary>1 h of driving every 13 h (gaps of 12 h, never ≥ 24 h) from <paramref name="first"/> to 13 h before the service.</summary>
    private static List<DrivingPeriod> NoWeeklyRestSince(DateTime first)
    {
        var history = new List<DrivingPeriod>();
        for (var start = first; start <= TripStart.AddHours(-2); start = start.AddHours(13))
            history.Add(new DrivingPeriod(1, start, start.AddHours(1)));
        return history;
    }

    private static ComplianceViolation? Find(ComplianceCheckResult result, string ruleId) =>
        result.Violations.SingleOrDefault(v => v.RuleId == ruleId);

    [Fact]
    public void Validate_EmptyHistory_CompliantWithSingleUnverifiableWarning()
    {
        var result = LegalComplianceEvaluation.Validate(Array.Empty<DrivingPeriod>(), TripStart, TripDuration);

        Assert.True(result.IsCompliant);
        var warning = Assert.Single(result.Violations);
        Assert.Equal(UnverifiableRuleId, warning.RuleId);
        Assert.Equal(ViolationSeverity.Warning, warning.Severity);
        Assert.Equal(
            "Historial insuficiente para verificar el descanso semanal: confirma el tacógrafo del conductor antes de la salida",
            warning.Message.Es());
    }

    [Fact]
    public void Validate_SingleRecord_UnverifiableWarningNotCritical()
    {
        var history = new[] { new DrivingPeriod(1, TripStart.AddHours(-16), TripStart.AddHours(-14)) };

        var result = LegalComplianceEvaluation.Validate(history, TripStart, TripDuration);

        Assert.True(result.IsCompliant);
        Assert.NotNull(Find(result, UnverifiableRuleId));
        Assert.Null(Find(result, WeeklyRestRuleId));
    }

    [Fact]
    public void Validate_RecordsOnlyInLast72hWithoutRest_UnverifiableWarning()
    {
        var result = LegalComplianceEvaluation.Validate(NoWeeklyRestSince(TripStart.AddHours(-72)), TripStart, TripDuration);

        Assert.NotNull(Find(result, UnverifiableRuleId));
        Assert.Null(Find(result, WeeklyRestRuleId));
    }

    [Fact]
    public void Validate_DailyRecordsSevenDaysWithoutRest_CriticalWeeklyRest()
    {
        var result = LegalComplianceEvaluation.Validate(NoWeeklyRestSince(TripStart.AddDays(-7)), TripStart, TripDuration);

        Assert.False(result.IsCompliant);
        Assert.Equal(ViolationSeverity.Critical, Find(result, WeeklyRestRuleId)!.Severity);
        Assert.Null(Find(result, UnverifiableRuleId));
    }

    [Fact]
    public void Validate_FirstRecordExactlySixTimes24hBefore_CriticalWeeklyRest()
    {
        var result = LegalComplianceEvaluation.Validate(NoWeeklyRestSince(TripStart.AddHours(-144)), TripStart, TripDuration);

        Assert.False(result.IsCompliant);
        Assert.NotNull(Find(result, WeeklyRestRuleId));
        Assert.Null(Find(result, UnverifiableRuleId));
    }

    [Fact]
    public void Validate_FirstRecordOneMinuteLaterThanSixTimes24h_UnverifiableWarning()
    {
        var result = LegalComplianceEvaluation.Validate(
            NoWeeklyRestSince(TripStart.AddHours(-144).AddMinutes(1)), TripStart, TripDuration);

        Assert.NotNull(Find(result, UnverifiableRuleId));
        Assert.Null(Find(result, WeeklyRestRuleId));
    }

    [Fact]
    public void Validate_RestFoundSixDaysAgo_CriticalUnchanged()
    {
        // A weekly rest found 6 or more days ago is critical, as it already was (SPEC 0040 R5).
        var history = new List<DrivingPeriod>
        {
            new(1, TripStart.AddDays(-9), TripStart.AddDays(-9).AddHours(1)),
        };
        history.AddRange(NoWeeklyRestSince(TripStart.AddDays(-6)));

        var result = LegalComplianceEvaluation.Validate(history, TripStart, TripDuration);

        Assert.NotNull(Find(result, WeeklyRestRuleId));
        Assert.Null(Find(result, UnverifiableRuleId));
    }

    [Fact]
    public void ToSnapshot_OnlyWarnings_CompliantWithWarnings()
    {
        var snapshot = LegalComplianceEvaluation.Validate(Array.Empty<DrivingPeriod>(), TripStart, TripDuration).ToSnapshot();

        Assert.True(snapshot.IsCompliant);
        Assert.Contains(snapshot.Warnings, w => w.Es().StartsWith("Historial insuficiente", StringComparison.Ordinal));
        Assert.Empty(snapshot.Violations);
    }

    [Fact]
    public void ToSnapshot_WithCritical_NonCompliant()
    {
        var snapshot = LegalComplianceEvaluation.Validate(NoWeeklyRestSince(TripStart.AddDays(-7)), TripStart, TripDuration).ToSnapshot();

        Assert.False(snapshot.IsCompliant);
        Assert.NotEmpty(snapshot.Violations);
    }
}
