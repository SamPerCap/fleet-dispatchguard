using System.Globalization;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Rules;

public enum RuleKind
{
    /// <summary>Legal: the allocation is rejected if it is broken.</summary>
    Blocking,
    /// <summary>Legal, but only warns, e.g. not verifiable with a short history (SPEC 0040).</summary>
    Warning,
    /// <summary>Orders candidates, never excludes.</summary>
    Ranking,
    /// <summary>Does not take part in allocation today.</summary>
    Informative
}

public sealed record RuleDto(string Id, LocalizedText Name, LocalizedText Reference, LocalizedText Explanation, LocalizedText Figure, RuleKind Kind);

public sealed record RuleGroupDto(string Key, LocalizedText Title, IReadOnlyList<RuleDto> Rules);

/// <summary>
/// Every figure is read from its single source (<see cref="DrivingLimits"/>, <see cref="ProximityRankingPolicy.RadiusBands"/>,
/// <see cref="ReturnToBaseService.Window"/>, <see cref="DriverCandidateSearch.DefaultMaxResults"/>). Never decides anything
/// (SPEC 0054).
/// </summary>
public static class RulesCatalog
{
    public static IReadOnlyList<RuleGroupDto> Build()
    {
        var bands = ProximityRankingPolicy.RadiusBands.Select(b => b.MaxKm).ToList();


        return
        [
            new("driving", Texts.GroupDriving(),
            [
                new("continuous", Texts.RuleContinuous(), Reference("CE 561/2006 Art. 7"),
                    Texts.RuleContinuousWhy(Hours(DrivingLimits.MaxContinuousDriving)),
                    Texts.RuleContinuousFigure(Hours(DrivingLimits.MaxContinuousDriving), Hours(DrivingLimits.MandatoryContinuousBreak)), RuleKind.Blocking),
                new("daily", Texts.DailyDriving(), Reference("CE 561/2006 Art. 6.1"),
                    Texts.RuleDailyWhy(), Hours(DrivingLimits.MaxDailyDriving), RuleKind.Blocking),
                new("daily-extended", Texts.RuleExtended(), Reference("CE 561/2006 Art. 6.1"),
                    Texts.RuleExtendedWhy(BothClocks),
                    Texts.RuleExtendedFigure(Hours(DrivingLimits.MaxDailyDrivingExtended), DrivingLimits.MaxExtendedDaysPerWeek), RuleKind.Blocking),
                new("weekly", Texts.WeeklyDriving(), Reference("CE 561/2006 Art. 6.2"),
                    Texts.RuleWeeklyWhy(BothClocks), Hours(DrivingLimits.MaxWeeklyDriving), RuleKind.Blocking),
                new("biweekly", Texts.TwoWeeksDriving(), Reference("CE 561/2006 Art. 6.3"),
                    Texts.RuleTwoWeeksWhy(BothClocks), Hours(DrivingLimits.MaxBiWeeklyDriving), RuleKind.Blocking),
            ]),
            new("rest", Texts.GroupRest(),
            [
                new("daily-rest", Texts.RuleDailyRest(), Reference("CE 561/2006 Art. 8.2"),
                    Texts.RuleDailyRestWhy(Hours(DrivingLimits.DailyRestWindow)),
                    Texts.RuleDailyRestFigure(Hours(DrivingLimits.DailyRestNormal), Hours(DrivingLimits.DailyRestWindow)), RuleKind.Blocking),
                new("daily-rest-reduced", Texts.RuleReducedRest(), Reference("CE 561/2006 Art. 8.4"),
                    Texts.RuleReducedRestWhy(),
                    Texts.RuleReducedRestFigure(Hours(DrivingLimits.DailyRestReduced), DrivingLimits.MaxReducedDailyRests), RuleKind.Blocking),
                new("daily-rest-split", Texts.RuleSplitRest(), Reference("CE 561/2006 Art. 4(g)"),
                    Texts.RuleSplitRestWhy(),
                    Texts.RuleSplitRestFigure(Hours(DrivingLimits.SplitDailyRestFirstPart), Hours(DrivingLimits.DailyRestReduced)), RuleKind.Blocking),
                new("weekly-rest", Texts.RuleWeeklyRest(), Reference("CE 561/2006 Art. 8.6"),
                    Texts.RuleWeeklyRestWhy(),
                    Texts.RuleWeeklyRestFigure(Hours(DrivingLimits.WeeklyRestNormal), Hours(DrivingLimits.WeeklyRestReduced),
                        DrivingLimits.MaxIntervalBetweenWeeklyRests.TotalDays), RuleKind.Blocking),
                new("weekly-rest-unverifiable", Texts.RuleUnverifiable(), Reference("CE 561/2006 Art. 8.6"),
                    Texts.RuleUnverifiableWhy(),
                    Texts.RuleUnverifiableFigure(DrivingLimits.MaxIntervalBetweenWeeklyRests.TotalDays), RuleKind.Warning),
            ]),
            new("later", Texts.GroupLater(),
            [
                new("later-services", Texts.LaterServices(), Texts.RuleLaterReference(),
                    Texts.RuleLaterWhy(), Texts.RuleLaterFigure(), RuleKind.Blocking),
            ]),
            new("return", Texts.GroupReturn(),
            [
                new("return-to-base", Texts.RuleReturn(), Texts.RuleReturnReference(),
                    Texts.RuleReturnWhy(), Every(ReturnToBaseService.Window), RuleKind.Informative),
            ]),
            new("overlap", Texts.GroupOverlap(),
            [
                new("driver-overlap", Texts.RuleDriverOverlap(), Texts.EngineRule(), Texts.RuleDriverOverlapWhy(), Texts.NoMargin(), RuleKind.Blocking),
                new("vehicle-overlap", Texts.RuleVehicleOverlap(), Texts.EngineRule(), Texts.RuleVehicleOverlapWhy(), Texts.NoMargin(), RuleKind.Blocking),
                new("driver-active", Texts.RuleDriverActive(), Texts.EngineRuleOf("SPEC 0066"),
                    Texts.RuleDriverActiveWhy(), Texts.Active(), RuleKind.Blocking),
                new("driver-unavailable", Texts.DriverAvailable(), Texts.EngineRuleOf("SPEC 0066"),
                    Texts.RuleDriverAvailableWhy(), Texts.NoUnavailability(), RuleKind.Blocking),
            ]),
            new("vehicle", Texts.GroupVehicle(),
            [
                new("vehicle-active", Texts.RuleVehicleActive(), Texts.EngineRule(), Texts.RuleVehicleActiveWhy(), Texts.Active(), RuleKind.Blocking),
                new("vehicle-seats", Texts.RuleSeats(), Texts.EngineRule(), Texts.RuleSeatsWhy(), Texts.RuleSeatsFigure(), RuleKind.Blocking),
                new("vehicle-unavailable", Texts.RuleVehicleAvailable(), Texts.EngineRule(),
                    Texts.RuleVehicleAvailableWhy(), Texts.NoUnavailability(), RuleKind.Blocking),
            ]),
            new("ranking", Texts.GroupRanking(),
            [
                new("same-booking", Texts.SameBooking(), Reference("SPEC 0045"), Texts.RuleSameBookingWhy(), Texts.Criterion1(), RuleKind.Ranking),
                new("base-match", Texts.RuleBaseMatch(), Reference("SPEC 0031"), Texts.RuleBaseMatchWhy(), Texts.Criterion2(), RuleKind.Ranking),
                new("proximity", Texts.RuleProximity(), Reference("SPEC 0031"), Texts.RuleProximityWhy(bands[^1]), Texts.Radii(bands), RuleKind.Ranking),
                new("tie-break", Texts.RuleTieBreak(), Reference("CE 561/2006 Art. 8.6 · SPEC 0047"),
                    Texts.RuleTieBreakWhy(), Texts.FewerHours(), RuleKind.Ranking),
                new("suggestions", Texts.RuleSuggestions(), Texts.Configuration(),
                    Texts.RuleSuggestionsWhy(), Texts.Number(DriverCandidateSearch.DefaultMaxResults), RuleKind.Ranking),
            ]),
        ];
    }

    /// <summary>The Regulation names no time zone (SPEC 0064 R1).</summary>
    private static LocalizedText BothClocks => Texts.BothClocks();

    /// <summary>An article or a SPEC: the same in every language.</summary>
    private static LocalizedText Reference(string reference) => Texts.AsWritten(reference);

    /// <summary>4.5 h → "4 h 30 min"; 45 min → "45 min"; 9 h → "9 h".</summary>
    public static LocalizedText Hours(TimeSpan time)
    {
        var hours = (int)time.TotalHours;
        return (hours, time.Minutes) switch
        {
            (0, var minutes) => Texts.Minutes(minutes),
            (var wholeHours, 0) => Texts.WholeHours(wholeHours),
            (var wholeHours, var minutes) => Texts.HoursAndMinutes(wholeHours, minutes),
        };
    }

    private static LocalizedText Every(TimeSpan time) =>
        time.TotalDays % 7 == 0 ? Texts.EveryWeeks(time.TotalDays / 7, time.TotalDays) : Texts.EveryDays(time.TotalDays);
}
