using System.Globalization;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.Rules;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Rules;

/// <summary>The catalog's figures come from the single sources, never hand-written (SPEC 0054 R2).</summary>
public class RulesCatalogTests
{
    private static RuleDto Rule(string id) => RulesCatalog.Build().SelectMany(g => g.Rules).Single(r => r.Id == id);

    [Theory]
    [InlineData(4.5, "4 h 30 min")]
    [InlineData(0.75, "45 min")]
    [InlineData(9, "9 h")]
    [InlineData(56, "56 h")]
    public void Hours_FormatsHoursAndMinutes(double hours, string expected) =>
        Assert.Equal(expected, RulesCatalog.Hours(TimeSpan.FromHours(hours)).Es());

    [Fact]
    public void Build_DrivingFigures_ComeFromDrivingLimits()
    {
        Assert.Equal($"{RulesCatalog.Hours(DrivingLimits.MaxContinuousDriving)} + pausa de {RulesCatalog.Hours(DrivingLimits.MandatoryContinuousBreak)}", Rule("continuous").Figure.Es());
        Assert.Equal(RulesCatalog.Hours(DrivingLimits.MaxDailyDriving), Rule("daily").Figure);
        Assert.Equal(RulesCatalog.Hours(DrivingLimits.MaxWeeklyDriving), Rule("weekly").Figure);
        Assert.Equal(RulesCatalog.Hours(DrivingLimits.MaxBiWeeklyDriving), Rule("biweekly").Figure);
        Assert.Contains($"máx. {DrivingLimits.MaxExtendedDaysPerWeek} veces", Rule("daily-extended").Figure.Es());
        Assert.Contains($"máx. {DrivingLimits.MaxReducedDailyRests} veces", Rule("daily-rest-reduced").Figure.Es());
        Assert.StartsWith(RulesCatalog.Hours(DrivingLimits.WeeklyRestNormal).Es(), Rule("weekly-rest").Figure.Es());
    }

    [Fact]
    public void Build_RankingAndReturnFigures_ComeFromTheirSources()
    {
        var bands = string.Join(" / ", ProximityRankingPolicy.RadiusBands.Select(b => b.MaxKm)) + " km";
        Assert.Equal(bands, Rule("proximity").Figure.Es());
        Assert.Equal(DriverCandidateSearch.DefaultMaxResults.ToString(CultureInfo.InvariantCulture), Rule("suggestions").Figure.Es());
        Assert.Contains($"{ReturnToBaseService.Window.TotalDays:0} días", Rule("return-to-base").Figure.Es());
    }

    [Fact]
    public void Build_DistinguishesBlockingFromRankingAndInformative()
    {
        var groups = RulesCatalog.Build();
        Assert.Equal(["driving", "rest", "later", "return", "overlap", "vehicle", "ranking"], groups.Select(g => g.Key));
        Assert.All(groups.Single(g => g.Key == "ranking").Rules, r => Assert.Equal(RuleKind.Ranking, r.Kind));
        Assert.All(groups.Single(g => g.Key == "driving").Rules, r => Assert.Equal(RuleKind.Blocking, r.Kind));
        Assert.Equal(RuleKind.Informative, Rule("return-to-base").Kind); // Not in AllocateDriver today.
        Assert.Equal(RuleKind.Warning, Rule("weekly-rest-unverifiable").Kind);
        Assert.Equal(groups.SelectMany(g => g.Rules).Count(), groups.SelectMany(g => g.Rules).Select(r => r.Id).Distinct().Count());
    }
}
