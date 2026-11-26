using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>Assigning a trip must not make a later, already assigned trip of the driver illegal, or worse (SPEC 0063).</summary>
[Trait("Regulation", "Arts. 6 to 8")]
public class LaterServicesEvaluationTests
{
    private static readonly DateTime Monday = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int day, double hour) => Monday.AddDays(day).AddHours(hour);

    private static DrivingPeriod Period(int day, double hour, double hours, int? tripId = null) => new(1, At(day, hour), At(day, hour + hours), tripId);

    /// <summary>Work long before, so the weekly rest is verifiable and found.</summary>
    private static DrivingPeriod Old => Period(-9, 8, 2);

    private static LaterServicesCheck Evaluate(IEnumerable<DrivingPeriod> calendar, DrivingPeriod trip) =>
        LaterServicesEvaluation.Evaluate(calendar.ToList(), trip);

    private static LaterServiceImpact Single(LaterServicesCheck check, string rulePrefix) =>
        Assert.Single(check.Impacts, i => i.With.RuleId.StartsWith("CE_561_2006." + rulePrefix, StringComparison.Ordinal));

    [Fact]
    public void Evaluate_TripRightBeforeLaterOne_BreaksContinuousDrivingOfTheLater()
    {
        var check = Evaluate([Old, Period(0, 12, 4, 20)], Period(0, 10, 2, 10));

        var impact = Single(check, "ContinuousDriving");
        Assert.Equal(20, impact.TripId);
        Assert.Equal(6, impact.With.Actual!.Value, 3);
        Assert.Equal(4, impact.Without!.Actual!.Value, 3);
    }

    [Theory]
    [InlineData(44, true)]  // 44 min: not a break
    [InlineData(45, false)] // exactly the mandatory break
    public void Evaluate_BreakBeforeLaterOne_DecidesContinuousDriving(int breakMinutes, bool broken)
    {
        var trip = new DrivingPeriod(1, At(0, 8), At(0, 12).AddMinutes(-breakMinutes), 10);

        var check = Evaluate([Old, Period(0, 12, 4, 20)], trip);

        Assert.Equal(broken, check.Impacts.Any(i => i.With.RuleId.Contains("ContinuousDriving")));
    }

    [Theory]
    [InlineData(3.0, false)] // 3 + 4 + 3 = 10 h: extended day
    [InlineData(3.1, true)]  // 10.1 h
    public void Evaluate_EarlierTripSameDay_LimitsDailyDrivingOfTheLater(double hours, bool broken)
    {
        var check = Evaluate([Old, Period(2, 13, 4, 20), Period(2, 18, 3, 21)], Period(2, 8, hours, 10));

        Assert.Equal(broken, check.HasImpacts);
        if (broken) Assert.Equal(21, Single(check, "DailyDriving").TripId);
    }

    [Fact]
    public void Evaluate_ThirdExtendedDayOfTheWeek_BreaksALaterExtendedDay()
    {
        // Monday 9.6 h (1st extended), Tuesday 9 h + trip 0.9 h = 9.9 h (2nd), Thursday 9.6 h would be the 3rd.
        List<DrivingPeriod> calendar =
        [
            Old, Period(0, 6, 4.5), Period(0, 11.5, 4.5), Period(0, 17, 0.6), Period(1, 6, 3), Period(1, 10, 3), Period(1, 14, 3),
            Period(3, 6, 4.5, 30), Period(3, 11.5, 4.5, 31), Period(3, 17, 0.6, 32)
        ];

        var impact = Single(Evaluate(calendar, Period(1, 18, 0.9, 10)), "DailyDriving");

        Assert.Equal(32, impact.TripId);
        Assert.Equal(9.6, impact.With.Actual!.Value, 3);
        Assert.Equal(9, impact.With.Limit);
        Assert.Equal(10, impact.Without!.Limit);
    }

    [Theory]
    [InlineData(4.0, false)] // 45 + 7 + 4 = 56 h
    [InlineData(4.1, true)]
    public void Evaluate_EarlierTripSameWeek_LimitsWeeklyDrivingOfTheLater(double hours, bool broken)
    {
        List<DrivingPeriod> calendar = [Old];
        for (var d = 1; d <= 5; d++) calendar.AddRange([Period(d, 7, 4.5), Period(d, 12.5, 4.5)]);
        calendar.AddRange([Period(6, 7, 3, 60), Period(6, 11, 4, 61)]);

        var check = Evaluate(calendar, Period(0, 8, hours, 10));

        Assert.Equal(broken, check.Impacts.Any(i => i.TripId == 61 && i.With.RuleId.Contains("WeeklyDriving.56")));
    }

    [Fact]
    public void Evaluate_EarlierTripWithin14Days_LimitsBiWeeklyDrivingOfTheLater()
    {
        // 9 h × 5 days + 8.9 h × 5 days = 89.5 h; the trip adds 1 h on the first Saturday.
        List<DrivingPeriod> calendar = [Old];
        for (var d = 0; d < 5; d++) calendar.AddRange([Period(d, 6, 4.5), Period(d, 11.5, 4.5)]);
        for (var d = 7; d < 12; d++) calendar.AddRange([Period(d, 6, 4.5), Period(d, 11.5, 4.4, 200 + d)]);

        var impact = Single(Evaluate(calendar, Period(5, 8, 1, 10)), "BiWeeklyDriving");

        Assert.Equal(211, impact.TripId);
        Assert.Equal(90.5, impact.With.Actual!.Value, 3);
        Assert.Equal(89.5, impact.Without!.Actual!.Value, 3);
    }

    [Fact]
    public void Evaluate_TripThatRemovesTheOnlyWeeklyRest_BreaksLaterWeeklyRest()
    {
        // Every day worked except day 0: the trip fills the only gap of 24 h or more.
        List<DrivingPeriod> calendar = [Old, .. Enumerable.Range(-5, 5).Select(d => Period(d, 8, 4)), .. Enumerable.Range(1, 4).Select(d => Period(d, 8, 4, 100 + d))];

        var check = Evaluate(calendar, Period(0, 9, 2, 10));

        Assert.Contains(check.Impacts, i => i.TripId == 102 && i.With.RuleId.Contains("WeeklyRest.45hours"));
    }

    [Fact]
    public void Evaluate_ReducedRestsBeyondTwoDays_AreSeenByTheLaterTrip()
    {
        // 06:00–20:00 days end with a 10 h (reduced) rest. Day 0 is free, so two groups of at most 3 reduced rests;
        // the trip on day 0 removes the weekly rest between them and joins both groups.
        static List<DrivingPeriod> Day(int day, int? id = null) => [Period(day, 6, 2), Period(day, 18, 2, id)];
        List<DrivingPeriod> calendar = [Old, .. Day(-3), .. Day(-2), .. Day(-1), .. Day(1), .. Day(2), .. Day(3, 103), Period(4, 6, 1, 104)];

        var check = Evaluate(calendar, Period(0, 6, 2, 10));

        Assert.Contains(check.Impacts, i => i.TripId == 103 && i.With.RuleId.Contains("ReducedLimit"));
    }

    [Theory]
    [InlineData(-1, 1)] // starts 1 minute before the end of the horizon: its own lookback sees the trip
    [InlineData(0, 0)]
    public void Evaluate_Horizon_IsTheEngineLookbackAfterTheTripEnd(int minutes, int evaluated)
    {
        var trip = Period(0, 8, 2, 10);
        var start = trip.End.Add(LegalComplianceEvaluation.LookbackWindow).AddMinutes(minutes);

        var check = Evaluate([Old, new DrivingPeriod(1, start, start.AddHours(1), 20)], trip);

        Assert.Equal(evaluated, check.Evaluated);
        Assert.Equal(trip.End + LegalComplianceEvaluation.LookbackWindow, check.Until);
    }

    [Fact]
    public void Evaluate_EarlierAndOverlappingServices_AreNotLaterServices()
    {
        // The overlap belongs to the schedule check (409), never to this rule.
        var check = Evaluate([Period(0, 4, 2, 5), Period(0, 9, 2, 20)], Period(0, 8, 2, 10));

        Assert.Equal(0, check.Evaluated);
        Assert.False(check.HasImpacts);
    }

    private static List<DrivingPeriod> LaterAlreadyIllegal => [Old, Period(2, 6, 4.5), Period(2, 12, 4.5), Period(2, 18, 1.4, 20)]; // 10.4 h

    [Fact]
    public void Evaluate_LaterAlreadyIllegalAndUntouched_DoesNotBlock() =>
        Assert.False(Evaluate(LaterAlreadyIllegal, Period(0, 8, 2, 10)).HasImpacts);

    [Fact]
    public void Evaluate_LaterAlreadyIllegalAndWorse_Blocks()
    {
        // The other services of the day are affected too; the one already illegal goes from 10.4 to 12.4 h.
        var impact = Assert.Single(Evaluate(LaterAlreadyIllegal, Period(2, 2, 2, 10)).Impacts, i => i.TripId == 20 && i.With.RuleId.Contains("DailyDriving"));

        Assert.Equal(12.4, impact.With.Actual!.Value, 3);
        Assert.Equal(10.4, impact.Without!.Actual!.Value, 3);
    }

    [Fact]
    public void Evaluate_UnverifiableWeeklyRestThatStaysAWarning_DoesNotBlock() =>
        Assert.False(Evaluate([Period(1, 8, 4, 20)], Period(0, 8, 2, 10)).HasImpacts);

    [Fact]
    public void Evaluate_WarningThatBecomesCritical_Blocks()
    {
        // Days 1–6 worked without a 24 h gap: on day 6 the history does not cover 6×24 h (warning).
        // The trip on day 0 completes the window without any rest: critical.
        List<DrivingPeriod> calendar = [.. Enumerable.Range(1, 6).Select(d => Period(d, 8, 2, 100 + d))];

        var check = Evaluate(calendar, Period(0, 8, 2, 10));

        Assert.Contains(check.Impacts, i => i.TripId == 106 && i.With.RuleId.Contains("WeeklyRest.45hours")
            && i.Without!.Outcome == RuleCheckOutcome.Warning);
    }

    [Fact]
    public void Evaluate_TripAlreadyInTheCalendar_NeverBlocks()
    {
        var trip = Period(0, 10, 2, 10);

        var check = Evaluate([Old, trip, Period(0, 12, 4, 20)], trip); // the later one is illegal because of it, but nothing changes

        Assert.Equal(1, check.Evaluated);
        Assert.False(check.HasImpacts);
    }

    /// <summary>
    /// Services offered in random order: whatever is accepted, at the end every accepted service is legal against the whole
    /// calendar. Fixed seeds, so a failure is reproducible.
    /// </summary>
    [Fact]
    public void Accepted_InAnyOrder_LeavesEveryServiceLegal()
    {
        var broken = new List<string>();
        int accepted = 0, rejectedOnlyByLater = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var offered = Enumerable.Range(0, 60)
                .Select(i => Period(random.Next(0, 21), random.Next(0, 96) / 4.0, random.Next(2, 18) / 4.0, i + 1))
                .ToList();
            var calendar = new List<DrivingPeriod>();
            foreach (var trip in offered)
            {
                if (calendar.Any(p => p.Start < trip.End && p.End > trip.Start)) continue;
                var own = LegalComplianceEvaluation.Validate(Visible(calendar, trip), trip.Start, trip.Duration);
                if (!own.IsCompliant) continue;
                if (Evaluate(calendar, trip).HasImpacts) { rejectedOnlyByLater++; continue; }
                calendar.Add(trip);
                accepted++;
            }

            foreach (var service in calendar)
            {
                var others = calendar.Where(p => p.TripId != service.TripId).ToList();
                var violated = LegalComplianceEvaluation.Validate(Visible(others, service), service.Start, service.Duration)
                    .RuleChecks.Where(c => c.Outcome == RuleCheckOutcome.Violated).Select(c => c.RuleId).ToList();
                if (violated.Count > 0) broken.Add($"seed {seed} service {service.TripId}: {string.Join(", ", violated)}");
            }
        }

        Assert.True(accepted > 1000 && rejectedOnlyByLater > 100, $"{accepted} accepted, {rejectedOnlyByLater} rejected only by a later service");
        Assert.True(broken.Count == 0, $"{broken.Count} of {accepted}:\n" + string.Join("\n", broken.Take(20)));
    }

    /// <summary>What <c>LegalValidationService</c> shows the engine for a service.</summary>
    private static List<DrivingPeriod> Visible(IEnumerable<DrivingPeriod> calendar, DrivingPeriod trip) =>
        calendar.Where(p => p.Start < trip.End + LegalComplianceEvaluation.LookaheadWindow
            && p.End > trip.Start - LegalComplianceEvaluation.LookbackWindow).ToList();

    [Fact]
    public void Message_SaysServiceRuleFiguresAndLimit_InMadridTime()
    {
        var impact = Single(Evaluate([Old, Period(2, 13, 4, 20), Period(2, 18, 3, 21)], Period(2, 8, 4, 10)), "DailyDriving");

        // Wednesday 4 Nov 2026 18:00 UTC = 19:00 Madrid
        Assert.Equal("Servicio #21 (04/11 19:00), Art. 6.1 conducción diaria: 11 h con este servicio, 7 h sin él (máximo 10 h)", impact.Message.Es());
    }

    [Fact]
    public void Message_ChangedLimit_SaysBothLimits()
    {
        List<DrivingPeriod> calendar =
        [
            Old, Period(0, 6, 4.5), Period(0, 11.5, 4.5), Period(0, 17, 0.6), Period(1, 6, 3), Period(1, 10, 3), Period(1, 14, 3),
            Period(3, 6, 4.5, 30), Period(3, 11.5, 4.5, 31), Period(3, 17, 0.6, 32)
        ];

        var impact = Single(Evaluate(calendar, Period(1, 18, 0.9, 10)), "DailyDriving");

        Assert.Equal("Servicio #32 (05/11 18:00), Art. 6.1 conducción diaria: 9,6 h con este servicio (máximo 9 h), 9,6 h sin él (máximo 10 h)", impact.Message.Es());
    }

    [Fact]
    public void Message_WeeklyRestWithoutAnyRest_SaysThereIsNone()
    {
        List<DrivingPeriod> calendar = [.. Enumerable.Range(1, 6).Select(d => Period(d, 8, 2, 100 + d))];

        var impact = Assert.Single(Evaluate(calendar, Period(0, 8, 2, 10)).Impacts, i => i.TripId == 106);

        Assert.Contains("Art. 8.6 descanso semanal dentro de 6×24 h: ninguno en el historial con este servicio", impact.Message.Es());
    }

    [Fact]
    public void WithLaterServices_AddsCriticalViolationsAndMakesItNonCompliant()
    {
        var own = LegalComplianceEvaluation.Validate([Old], At(0, 10), TimeSpan.FromHours(2));
        Assert.True(own.IsCompliant);

        var result = own.WithLaterServices(Evaluate([Old, Period(0, 12, 4, 20)], Period(0, 10, 2, 10)));

        Assert.False(result.IsCompliant);
        Assert.Contains(result.Violations, v => v.RuleId == "LaterServices.CE_561_2006.ContinuousDriving.4_5hours");
        Assert.Equal(own.RuleChecks.Count, result.RuleChecks.Count);
        Assert.False(result.ToSnapshot().IsCompliant);
    }

    [Fact]
    public void WithLaterServices_NoImpacts_KeepsTheResultAndItsWarnings()
    {
        var own = LegalComplianceEvaluation.Validate([], At(0, 10), TimeSpan.FromHours(2)); // unverifiable weekly rest

        var result = own.WithLaterServices(Evaluate([], Period(0, 10, 2, 10)));

        Assert.True(result.IsCompliant);
        Assert.Equal(own.Violations.Count, result.Violations.Count);
        Assert.NotNull(result.LaterServices);
    }

    // Mutation testing (SPEC 0099): the daily rest fails below the reduced one and is a minimum; reduced rests are counted, not hours.
    [Fact]
    public void Message_DailyRestThatNoLongerFits_SaysTheReducedRestAsMinimum()
    {
        var impact = Assert.Single(Evaluate([Old, Period(1, 6, 4, 20)], Period(0, 18, 4, 10)).Impacts);

        Assert.Equal("Servicio #20 (03/11 07:00), Art. 8.2 descanso diario dentro de 24 h: 8 h con este servicio, 20 h sin él (mínimo 9 h)", impact.Message.Es());
    }

    [Fact]
    public void Message_ReducedRests_CountsRests()
    {
        static List<DrivingPeriod> Day(int day, int? id = null) => [Period(day, 6, 2), Period(day, 18, 2, id)];
        List<DrivingPeriod> calendar = [Old, .. Day(-3), .. Day(-2), .. Day(-1), .. Day(1), .. Day(2), .. Day(3, 103), Period(4, 6, 1, 104)];

        var impact = Assert.Single(Evaluate(calendar, Period(0, 6, 2, 10)).Impacts, i => i.TripId == 103 && i.With.RuleId.Contains("ReducedLimit"));

        Assert.Equal("Servicio #103 (05/11 19:00), Art. 8.4 descansos diarios reducidos entre descansos semanales: 12 descansos con este servicio, 6 descansos sin él (máximo 3 descansos)", impact.Message.Es());
    }

    [Fact]
    public void Message_LaterServiceWithoutId_SaysUnknown()
    {
        var impact = Assert.Single(Evaluate([Old, Period(0, 12, 4)], Period(0, 10, 2, 10)).Impacts);

        Assert.StartsWith("Servicio #? ", impact.Message.Es(), StringComparison.Ordinal);
    }

    // Mutation testing (SPEC 0099): impacts by departure whatever the order of the calendar; results only for affected services.
    [Fact]
    public void Evaluate_LaterServicesOutOfOrder_ImpactsByDeparture()
    {
        var check = Evaluate([Old, Period(0, 14.7, 1, 21), Period(0, 12, 2.6, 20)], Period(0, 10, 2, 10));

        Assert.Equal([20, 21], check.AffectedTripIds);
    }

    [Fact]
    public void Evaluate_OneLaterServiceUnaffected_HasNoResult()
    {
        var check = Evaluate([Old, Period(0, 12, 4, 20), Period(1, 12, 1, 21)], Period(0, 10, 2, 10));

        Assert.Equal([20], check.Results.Keys);
    }

    [Fact]
    public void Evaluate_NoCalendarOrNoTrip_NamesTheArgument()
    {
        Assert.Throws<ArgumentNullException>("calendar", () => LaterServicesEvaluation.Evaluate(null!, Period(0, 10, 2, 10)));
        Assert.Throws<ArgumentNullException>("trip", () => LaterServicesEvaluation.Evaluate([Old], null!));
    }
}
