using System.Globalization;
using DispatchGuard.Core.ValueObjects;
using Xunit.Abstractions;

namespace Core.Tests.ValueObjects;

/// <summary>CE 561/2006: week (Art. 4(i)) in Madrid time and UTC, driving between daily rests (4(k)) and two natural weeks (6.3) (SPEC 0064).</summary>
[Trait("Regulation", "Art. 6")]
public class Article6Tests(ITestOutputHelper output)
{
    private const string Daily = "CE_561_2006.DailyDriving.9hours";
    private const string Weekly = "CE_561_2006.WeeklyDriving.56hours";
    private const string BiWeekly = "CE_561_2006.BiWeeklyDriving.90hours";

    private static readonly DateTime Monday = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc); // Madrid = UTC+1

    private static DrivingPeriod Period(DateTime start, double hours, int? id = null) => new(1, start, start.AddMinutes(Math.Round(hours * 60)), id);

    private static DrivingPeriod Period(int day, double hour, double hours, int? id = null) => Period(Monday.AddDays(day).AddMinutes(Math.Round(hour * 60)), hours, id);

    private static ComplianceRuleCheck Check(IEnumerable<DrivingPeriod> history, DrivingPeriod trip, string rule) =>
        LegalComplianceEvaluation.Validate(history.ToList(), trip.Start, trip.Duration).RuleChecks.Single(c => c.RuleId == rule);

    /// <summary>Two 4.5 h blocks a day from 07:00 UTC on the given days (9 h).</summary>
    private static IEnumerable<DrivingPeriod> NineHours(params int[] days) => days.SelectMany(d => new[] { Period(d, 7, 4.5), Period(d, 12.5, 4.5) });

    [Theory]
    [InlineData("2026-11-08T22:59:00", WeekClock.Madrid, "2026-11-01T23:00:00")] // Sunday 23:59 in Madrid
    [InlineData("2026-11-08T23:00:00", WeekClock.Madrid, "2026-11-08T23:00:00")] // Monday 00:00 in Madrid
    [InlineData("2026-11-08T23:00:00", WeekClock.Utc, "2026-11-02T00:00:00")]
    [InlineData("2026-11-09T00:00:00", WeekClock.Utc, "2026-11-09T00:00:00")]
    [InlineData("2026-07-05T22:00:00", WeekClock.Madrid, "2026-07-05T22:00:00")] // summer: UTC+2
    public void StartOf_IsMondayMidnightOfThatClock(string instant, WeekClock clock, string expected) =>
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), DrivingWeek.StartOf(DateTime.Parse(instant, CultureInfo.InvariantCulture), clock));

    [Theory]
    [InlineData("2026-03-25T12:00:00", 167)] // the week of the March change
    [InlineData("2026-10-21T12:00:00", 169)] // the week of the October change
    [InlineData("2026-11-04T12:00:00", 168)]
    public void StartUtc_MadridWeeksFollowTheClockChange(string instant, int hours)
    {
        var monday = DrivingWeek.MondayOf(DateTime.Parse(instant, CultureInfo.InvariantCulture), WeekClock.Madrid);

        Assert.Equal(hours, (DrivingWeek.StartUtc(monday.AddDays(7), WeekClock.Madrid) - DrivingWeek.StartUtc(monday, WeekClock.Madrid)).TotalHours);
    }

    /// <summary>Whatever the instant, the lookback reaches the start of the previous week in both clocks (SPEC 0064 R7).</summary>
    [Fact]
    public void LookbackWindow_CoversTwoNaturalWeeksInBothClocksAllYear()
    {
        var longest = TimeSpan.Zero;
        for (var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); t.Year < 2028; t = t.AddMinutes(15))
            foreach (var clock in DrivingWeek.Clocks)
            {
                var previous = DrivingWeek.StartUtc(DrivingWeek.MondayOf(t, clock).AddDays(-7), clock);
                Assert.True(t - previous < LegalComplianceEvaluation.LookbackWindow, $"{t:u} {clock}");
                if (t - previous > longest) longest = t - previous;
            }

        Assert.True(longest > TimeSpan.FromDays(14)); // 14 days alone would not be enough
        Assert.Equal(DrivingWeek.LongestTwoWeeks, LegalComplianceEvaluation.LookbackWindow);
    }

    [Fact]
    public void Validate_ServiceAcrossMonday_CountsItsPartInTheNewWeek()
    {
        // Sunday 23:00 – Monday 01:00 UTC. New week: 1 h (UTC) / 2 h (Madrid) + 4 h.
        var check = Check([Period(-1, 23, 2)], Period(0, 10, 4), Weekly);

        Assert.Equal(6, check.Actual!.Value, 3); // the strictest: Madrid
        Assert.Equal("hora de Madrid", check.CountedIn.Es());
        Assert.Contains("Dos cuentas", check.Note.Es());
    }

    [Fact]
    public void Validate_ServiceAcrossBothMondays_IsSplitByTheClock()
    {
        // Sunday 22:00 – Monday 01:00 UTC. The new week gets 2 h in Madrid and 1 h in UTC, never the 3 h nor none.
        var check = Check([Period(-1, 22, 3)], Period(0, 10, 4), Weekly);

        Assert.Equal((6.0, "hora de Madrid"), (Math.Round(check.Actual!.Value, 3), check.CountedIn.Es()));
        Assert.Equal(3, Check([Period(-1, 22, 3)], Period(0, 10, 4), BiWeekly).Actual!.Value - 4, 3); // both weeks together hold it whole
    }

    [Fact]
    public void Validate_TripAcrossMonday_IsCheckedInEveryPairOfWeeksItTouches()
    {
        // Week of the 2nd: 18 h. The trip leaves it on Sunday and ends on Monday: the pair 2nd + 9th holds it whole.
        var check = Check(NineHours(0, 1), Period(6, 22, 3), BiWeekly);

        Assert.Equal((21.0, null), (Math.Round(check.Actual!.Value, 3), check.CountedIn.Es()));
        Assert.Contains("Semanas del 02/11 y del 09/11", check.Note.Es());
    }

    [Theory]
    [InlineData(2.0, RuleCheckOutcome.Passed)] // UTC 53 + 3 = 56 h
    [InlineData(2.25, RuleCheckOutcome.Violated)]
    public void Validate_TripAcrossMonday_IsCheckedInTheWeekItLeaves(double hoursBeforeMadridMonday, RuleCheckOutcome expected)
    {
        // Mon–Sat 45 + 8 = 53 h; the trip crosses Madrid's Monday (23:00 UTC) and ends at UTC's.
        List<DrivingPeriod> history = [.. NineHours(0, 1, 2, 3, 4), Period(5, 7, 4), Period(5, 12, 4)];
        var trip = Period(6, 23 - hoursBeforeMadridMonday, hoursBeforeMadridMonday + 1);

        var check = Check(history, trip, Weekly);

        Assert.Equal(53 + hoursBeforeMadridMonday + 1, check.Actual!.Value, 3);
        Assert.Equal("UTC", check.CountedIn.Es());
        Assert.Equal(expected, check.Outcome);
    }

    [Fact]
    public void Validate_LegalInMadridAndIllegalInUtc_IsRejected_AndTheOtherWayRound()
    {
        // 54 h Mon–Sat. Sunday 22:00–01:00 UTC is 23:00–02:00 in Madrid: 1 h of the old week there, 2 h in UTC.
        List<DrivingPeriod> history = [.. NineHours(0, 1, 2, 3, 4, 5)];
        var legalBoth = Check(history, Period(6, 22, 3), Weekly);
        Assert.Equal((RuleCheckOutcome.Passed, 56.0), (legalBoth.Outcome, legalBoth.Actual!.Value));

        var onlyMadrid = Check(history, Period(6, 21.75, 3), Weekly); // UTC 54 + 2.25; Madrid 54 + 1.25
        Assert.Equal(RuleCheckOutcome.Violated, onlyMadrid.Outcome);
        Assert.Equal("UTC", onlyMadrid.CountedIn.Es());

        // The new week, full in Madrid only: Monday 00:00–01:00 Madrid is still Sunday in UTC.
        List<DrivingPeriod> next = [Period(-1, 23, 1), .. NineHours(0, 1, 2, 3, 4, 5)]; // Madrid 1 + 54; UTC 54
        var onlyUtc = Check(next, Period(6, 10, 1.5), Weekly);
        Assert.Equal(RuleCheckOutcome.Violated, onlyUtc.Outcome);
        Assert.Equal((56.5, "hora de Madrid"), (onlyUtc.Actual!.Value, onlyUtc.CountedIn.Es()));
    }

    [Fact]
    public void Validate_BothCountsEqual_SaysNothingAboutClocks()
    {
        var check = Check([Period(0, 7, 4)], Period(0, 12, 4), Weekly);

        Assert.Null(check.CountedIn);
        Assert.DoesNotContain("UTC", check.Note.Es() ?? "");
        Assert.Contains("Semana del 02/11", check.Note.Es());
    }

    [Fact]
    public void Violation_WhenTheCountsDiffer_SaysWhichTimeItIsCountedIn()
    {
        List<DrivingPeriod> history = [.. NineHours(0, 1, 2, 3, 4, 5)];

        var result = LegalComplianceEvaluation.Validate(history, Monday.AddDays(6).AddHours(21.75), TimeSpan.FromHours(3));

        Assert.Contains(result.Violations, v => v.Message.Es() == "Conducción semanal: 56,25 h en UTC (máximo 56 h)");
    }

    [Theory]
    [InlineData(1.0, RuleCheckOutcome.Passed)]   // 4.5 + 4.5 + 1 = 10 h, extended
    [InlineData(1.5, RuleCheckOutcome.Violated)] // 10.5 h
    public void Validate_NightShift_AddsUpAcrossMidnight(double last, RuleCheckOutcome expected)
    {
        // Wednesday 18:00–22:30 and 23:30–04:00: the same working day for the 05:00 service.
        var check = Check([Period(2, 18, 4.5), Period(2, 23.5, 4.5)], Period(3, 5, last), Daily);

        Assert.Equal(9 + last, check.Actual!.Value, 3);
        Assert.Equal(expected, check.Outcome);
        Assert.Contains("Jornada desde el 04/11 19:00", check.Note.Es()); // Madrid
    }

    [Theory]
    [InlineData(9.0, 4.0)]  // a nine-hour rest closes the working day
    [InlineData(8.75, 8.0)] // 8 h 45 does not
    public void Validate_OnlyARestOfNineHoursClosesTheWorkingDay(double gap, double expected)
    {
        var first = Period(2, 2, 4); // 02:00–06:00
        var check = Check([first], Period(first.End.AddHours(gap), 4), Daily);

        Assert.Equal(expected, check.Actual!.Value, 3);
    }

    [Fact]
    public void Validate_SameCalendarDaySplitByARest_IsTwoWorkingDays()
    {
        // 00:00–04:00 and 13:00–17:30 of the same date: 9 h of rest between them.
        Assert.Equal(4.5, Check([Period(2, 0, 4)], Period(2, 13, 4.5), Daily).Actual!.Value, 3);
    }

    [Fact]
    public void Validate_ExtendedDayAcrossMonday_CountsInTheWeekItStarts_AndSaysItIsOurCriterion()
    {
        // Extended working days: Sunday night (starts in the old week), Monday and Tuesday nights. The third would be Wednesday.
        static IEnumerable<DrivingPeriod> Night(int day) => [Period(day, 18, 4.5), Period(day, 23.25, 4.5), Period(day + 1, 4.5, 0.5)]; // 9.5 h
        var check = Check([.. Night(-1), .. Night(0)], Period(2, 12, 4), Daily);

        Assert.Equal(10, check.Limit); // only one used this week: Sunday's belongs to the week before
        Assert.Contains("1 de 2 días ampliados", check.Note.Es());
        Assert.Contains("criterio propio, no texto legal", check.Note.Es());
    }

    [Fact]
    public void Validate_NoWorkingDayAcrossMonday_DoesNotMentionTheCriterion() =>
        Assert.DoesNotContain("criterio propio", Check([Period(1, 7, 4)], Period(1, 12, 4), Daily).Note.Es());

    [Fact]
    public void Validate_ExtendedQuota_UsesTheStrictestClock()
    {
        // Three extended days in Madrid's week: the first starts Monday 00:15 in Madrid (Sunday 23:15 UTC).
        List<DrivingPeriod> history =
        [
            Period(-1, 23.25, 4.5), Period(0, 4.5, 4.5), Period(0, 10, 0.5), // Madrid: Monday; UTC: Sunday
            Period(1, 7, 4.5), Period(1, 12.5, 4.5), Period(1, 18, 0.5),
            Period(2, 7, 4.5), Period(2, 12.5, 4.5)
        ];

        var check = Check(history, Period(2, 18, 0.5), Daily);

        Assert.Equal((RuleCheckOutcome.Violated, 9.0, 9.5), (check.Outcome, check.Limit!.Value, check.Actual!.Value));
        Assert.Contains("hora de Madrid", check.Note.Es());
    }

    [Theory]
    [InlineData(0.5, RuleCheckOutcome.Passed)] // 45 + 44.5 + 0.5 = 90
    [InlineData(0.75, RuleCheckOutcome.Violated)]
    public void Validate_PreviousAndCurrentWeek_NinetyHours(double hours, RuleCheckOutcome expected)
    {
        List<DrivingPeriod> history = [.. NineHours(0, 1, 2, 3, 4), .. NineHours(7, 8, 9, 10), Period(11, 7, 4.5), Period(11, 12.5, 4)];

        var check = Check(history, Period(12, 8, hours), BiWeekly);

        Assert.Equal(89.5 + hours, check.Actual!.Value, 3);
        Assert.Equal(expected, check.Outcome);
        Assert.Contains("Semanas del 02/11 y del 09/11", check.Note.Es());
    }

    [Fact]
    public void Validate_FourteenRollingDaysOver90_ButNotTwoNaturalWeeks_IsLegal()
    {
        // Wed–Sat of week 1 (36 h), Mon–Fri of week 2 (45 h), Monday of week 3 (9 h) and a Tuesday trip of 4 h: 94 h in 14 days,
        // but weeks 2 + 3 = 58 h.
        List<DrivingPeriod> history = [.. NineHours(2, 3, 4, 5), .. NineHours(7, 8, 9, 10, 11), .. NineHours(14)];

        Assert.Equal((RuleCheckOutcome.Passed, 58.0), (Check(history, Period(15, 8, 4), BiWeekly) is var check ? (check.Outcome, Math.Round(check.Actual!.Value, 3)) : default));
    }

    [Fact]
    public void Validate_BiWeekly_WithClockChange_CountsBothWeeksWhole()
    {
        // Madrid weeks of 12 and 19 October 2026 (the second has 169 h). Monday 12, 00:00–04:00 Madrid, seen from Sunday 25, 23:30 Madrid.
        var first = Period(new DateTime(2026, 10, 11, 22, 0, 0, DateTimeKind.Utc), 4);
        var trip = Period(new DateTime(2026, 10, 25, 22, 30, 0, DateTimeKind.Utc), 0.5);

        Assert.True(trip.Start - first.Start > TimeSpan.FromDays(14));
        Assert.Equal(4.5, Check(Visible([first], trip), trip, BiWeekly).Actual!.Value, 3);
    }

    [Fact]
    public void MeasureHours_UsesTheWorkingDayAndTheStrictestWeek()
    {
        List<DrivingPeriod> history = [Period(-1, 23, 2), Period(0, 4, 4)]; // Sunday 23:00–01:00 UTC, Monday 04:00–08:00

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(12));

        Assert.Equal((6.0, 6.0, 6.0), (hours.Today.TotalHours, hours.ThisWeek.TotalHours, hours.LastTwoWeeks.TotalHours));
        Assert.Equal(0, LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(17)).Today.TotalHours); // 9 h of rest since 08:00
    }

    /// <summary>A service under way ended the rest when it started: the rest does not keep growing while it runs.</summary>
    [Theory]
    [InlineData(8.0, 4.0)] // 06:00–14:00 is not a daily rest: the morning belongs to the same working day
    [InlineData(9.0, 0.0)]
    public void MeasureHours_ServiceUnderWay_TheRestEndedWhenItStarted(double gap, double expectedToday)
    {
        List<DrivingPeriod> history = [Period(0, 2, 4), Period(0, 6 + gap, 4)];

        var hours = LegalComplianceEvaluation.MeasureHours(history, Monday.AddHours(6 + gap + 3));

        Assert.Equal(expectedToday, hours.Today.TotalHours);
    }

    [Fact]
    public void LaterServices_AlreadyIllegalInOneClock_DoesNotHideTheOtherOne()
    {
        // Week of the 9th: 54 + 1 h. The forced service (Sunday 22:30–24:00 UTC) makes 56.5 h in UTC and 55.5 h in Madrid.
        List<DrivingPeriod> calendar = [.. NineHours(7, 8, 9, 10, 11, 12), Period(13, 8, 1), Period(13, 22.5, 1.5, 90)];
        var trip = Period(6, 23, 1, 99); // Monday 00:00–01:00 in Madrid: one more hour there, none in UTC

        var impact = Assert.Single(LaterServicesEvaluation.Evaluate(calendar, trip).Impacts, i => i.With.RuleId == Weekly);

        Assert.Equal(90, impact.TripId);
        Assert.EndsWith("56,5 h en hora de Madrid con este servicio, 55,5 h en hora de Madrid sin él (máximo 56 h)", impact.Message.Es());
    }

    /// <summary>
    /// Random calendars at any time of the day, over the weeks of both clock changes and an ordinary one. For each offered
    /// service the engine (its own rules and the later services, SPEC 0063) must say exactly what the oracle says of the
    /// calendar with it, for Art. 6. Only Art. 6 decides here, so that its limits are really reached.
    /// </summary>
    [Theory]
    [InlineData(2026, 3, 16)]
    [InlineData(2026, 10, 12)]
    [InlineData(2026, 11, 2)]
    public void Engine_AgreesWithAnIndependentReadingOfArticle6(int year, int month, int day)
    {
        var start = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var disagreements = new List<string>();
        var broken = new Dictionary<string, int>();
        int offered = 0, clocksDiffer = 0;
        for (var seed = 0; seed < 30; seed++)
        {
            var random = new Random(seed);
            var calendar = new List<DrivingPeriod>();
            // Full working days (4.25 + 4.25 + up to 1.5 h) at the seed's time of day, plus loose services, in random order.
            var phase = random.Next(0, 96) / 4.0;
            var full = Enumerable.Range(0, 28).SelectMany(d => new[] { (0.0, 17), (5.0, 17), (10.0, random.Next(1, 7)) }
                .Select(b => (At: start.AddDays(d).AddHours(phase + b.Item1), Quarters: b.Item2)));
            var loose = Enumerable.Range(0, 60).Select(_ => (At: start.AddMinutes(15 * random.Next(0, 4 * 24 * 28)), Quarters: random.Next(2, 19)));
            var offers = full.Concat(loose).ToArray();
            random.Shuffle(offers);
            for (var i = 0; i < offers.Length; i++)
            {
                var trip = Period(offers[i].At, offers[i].Quarters / 4.0, i + 1);
                if (calendar.Any(p => p.Start < trip.End && p.End > trip.Start)) continue;
                offered++;

                var oracle = Article6Oracle.Broken([.. calendar, trip]);
                var engine = EngineArticle6(calendar, trip);
                if (!engine.SetEquals(oracle)) // the same articles, not only the same verdict
                    disagreements.Add($"seed {seed} #{i + 1} {trip}: engine [{string.Join(",", engine)}], text [{string.Join(",", oracle)}]");
                foreach (var article in oracle) broken[article] = broken.GetValueOrDefault(article) + 1;
                if (LegalComplianceEvaluation.Validate(Visible(calendar, trip), trip.Start, trip.Duration).RuleChecks.Any(c => c.CountedIn is not null)) clocksDiffer++;
                if (oracle.Count == 0) calendar.Add(trip);
            }
        }

        // The cover quotes this line (SPEC 0096).
        output.WriteLine($"{offered} services compared article by article, {disagreements.Count} disagreements, {clocksDiffer} counted differently in the two clocks");
        Assert.True(disagreements.Count == 0, $"{disagreements.Count} of {offered}:\n" + string.Join("\n", disagreements.Take(10)));
        Assert.All(new[] { "6.1", "6.2", "6.3" }, a => Assert.True(broken.GetValueOrDefault(a) > 5, $"{a}: {broken.GetValueOrDefault(a)}"));
        Assert.True(clocksDiffer > 20, $"{clocksDiffer} with different counts");
    }

    private static HashSet<string> EngineArticle6(IReadOnlyList<DrivingPeriod> calendar, DrivingPeriod trip)
    {
        static bool IsArticle6(ComplianceRuleCheck check) => check.RuleId is Daily or Weekly or BiWeekly;
        var own = LegalComplianceEvaluation.Validate(Visible(calendar, trip), trip.Start, trip.Duration).RuleChecks
            .Where(c => IsArticle6(c) && c.Outcome == RuleCheckOutcome.Violated).Select(c => c.Article);
        var later = LaterServicesEvaluation.Evaluate(calendar, trip).Impacts.Where(i => IsArticle6(i.With)).Select(i => i.With.Article);
        return own.Concat(later).Select(a => a["Art. ".Length..]).ToHashSet();
    }

    /// <summary>What <c>LegalValidationService</c> shows the engine for a service.</summary>
    private static List<DrivingPeriod> Visible(IEnumerable<DrivingPeriod> calendar, DrivingPeriod trip) =>
        calendar.Where(p => p.Start < trip.End + LegalComplianceEvaluation.LookaheadWindow
            && p.End > trip.Start - LegalComplianceEvaluation.LookbackWindow).ToList();
}
