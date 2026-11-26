using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// CE 561/2006 rule engine: evaluates a trip against the driving history of a driver. Stateless from the outside; the instance
/// only holds what one evaluation accumulates.
/// </summary>
public sealed class LegalComplianceEvaluation
{
    /// <summary>
    /// Two whole natural weeks in both clocks (Art. 6.3, SPEC 0064 R7). Also the horizon of the later services that see a trip
    /// (SPEC 0063).
    /// </summary>
    public static readonly TimeSpan LookbackWindow = DrivingWeek.LongestTwoWeeks;

    /// <summary>
    /// Trips already assigned after this one take part in the daily-rest window, so that the verdict does not depend on
    /// allocation order (SPEC 0041). Two days cover the working day that holds the trip.
    /// </summary>
    public static readonly TimeSpan LookaheadWindow = TimeSpan.FromDays(2);

    private readonly IReadOnlyList<DrivingPeriod> _orderedHistory;
    private readonly IReadOnlyList<DrivingPeriod> _fullCalendar;
    private readonly DrivingLimits _limits;
    private readonly CurrentDay _day;
    private readonly WeekLoad _week;
    private readonly WeekLoad _twoWeeks;
    private readonly DateTime _tripStart;
    private readonly TimeSpan _tripDuration;
    private readonly List<ComplianceViolation> _violations;
    private readonly List<ComplianceRuleCheck> _checks = new();

    private LegalComplianceEvaluation(
        IReadOnlyList<DrivingPeriod> orderedHistory,
        IReadOnlyList<DrivingPeriod> fullCalendar,
        DrivingLimits limits,
        CurrentDay day,
        DateTime tripStart,
        TimeSpan tripDuration)
    {
        _orderedHistory = orderedHistory;
        _fullCalendar = fullCalendar;
        _limits = limits;
        _day = day;
        _week = HeaviestWeeks(orderedHistory, tripStart, tripStart + tripDuration, 1);
        _twoWeeks = HeaviestWeeks(orderedHistory, tripStart, tripStart + tripDuration, 2);
        _tripStart = tripStart;
        _tripDuration = tripDuration;
        _violations = new();
    }

    /// <summary>
    /// <paramref name="drivingHistory"/> may include periods already assigned after the trip: only the daily-rest rule looks at
    /// them (SPEC 0041); every other rule uses periods starting before the trip.
    /// </summary>
    public static ComplianceCheckResult Validate(
        IReadOnlyList<DrivingPeriod> drivingHistory,
        DateTime tripStart,
        TimeSpan tripDuration)
    {
        ArgumentNullException.ThrowIfNull(drivingHistory);

        var fullCalendar = drivingHistory.OrderBy(p => p.Start).ToList();
        var orderedHistory = fullCalendar.Where(p => p.Start < tripStart).ToList();
        var day = DayOf(orderedHistory, tripStart, tripStart + tripDuration);
        var limits = BuildDrivingLimits(orderedHistory, tripStart, day);

        var evaluation = new LegalComplianceEvaluation(
            orderedHistory,
            fullCalendar,
            limits,
            day,
            tripStart,
            tripDuration);

        evaluation.EvaluateAllRules();

        var remaining = evaluation.CalculateRemaining();

        var result = evaluation._violations.Count == 0
            ? ComplianceCheckResult.Compliant(remaining.Today, remaining.ThisWeek, remaining.LastTwoWeeks)
            // Only critical violations block; warnings travel with a compliant result (SPEC 0040).
            : evaluation._violations.All(v => v.Severity == ViolationSeverity.Warning)
                ? ComplianceCheckResult.CompliantWithWarnings(
                    evaluation._violations, remaining.Today, remaining.ThisWeek, remaining.LastTwoWeeks)
                : ComplianceCheckResult.NonCompliant(
                    evaluation._violations,
                    remaining.Today,
                    remaining.ThisWeek,
                    remaining.LastTwoWeeks);

        return result.WithRuleChecks(evaluation._checks);
    }

    private void Record(
        string ruleId, string article, LocalizedText name, bool passed, double? actual, double? limit, RuleUnit unit = RuleUnit.Hours, LocalizedText? note = null) =>
        _checks.Add(new ComplianceRuleCheck(ruleId, article, name, passed ? RuleCheckOutcome.Passed : RuleCheckOutcome.Violated, actual, limit, unit, note));

    /// <summary>
    /// In this order, the one of the explanations: continuous (Art. 7: 4.5h), daily (Art. 6.1: 9h, or 10h at most twice a week),
    /// weekly (Art. 6.2: 56h) and bi-weekly (Art. 6.3: 90h) driving; daily rest (Art. 8.2 24h window: 11h, 9h reduced at most 3
    /// times, or 3h+9h split; SPEC 0041) and weekly rest (Art. 8.6: 45h, or 24h reduced, within 6×24h).
    /// </summary>
    private void EvaluateAllRules()
    {
        EvaluateContinuousDriving();
        EvaluateDailyDriving();
        EvaluateWeeklyDriving();
        EvaluateBiWeeklyDriving();
        EvaluateDailyRest();
        EvaluateWeeklyRest();
    }

    private void EvaluateContinuousDriving()
    {
        var projected = _limits.DrivingSinceLastBreak + _tripDuration;
        var passed = _limits.CanDriveContinuous(_tripDuration);
        Record("CE_561_2006.ContinuousDriving.4_5hours", "Art. 7", Texts.ContinuousDriving(),
            passed, projected.TotalHours, DrivingLimits.MaxContinuousDriving.TotalHours);

        if (!passed)
        {
            _violations.Add(ComplianceViolation.Critical(
                "CE_561_2006.ContinuousDriving.4_5hours",
                Texts.ContinuousDrivingExceeded(projected.TotalHours, DrivingLimits.MaxContinuousDriving.TotalHours)));
        }
    }

    private void EvaluateDailyDriving()
    {
        var maxToday = _limits.CanUseExtendedDay()
            ? DrivingLimits.MaxDailyDrivingExtended
            : DrivingLimits.MaxDailyDriving;

        // The working day, from the end of one daily rest to the next (Art. 4(k)), not the calendar day (SPEC 0064 R3).
        var total = _limits.DrivingToday + _tripDuration;
        var passed = total <= maxToday;
        var quota = _day.Quota;
        var notes = new List<LocalizedText>
        {
            Texts.WorkingDaySince(_day.Start),
            maxToday == DrivingLimits.MaxDailyDrivingExtended
                ? Texts.ExtendedLimit(quota.Used, DrivingLimits.MaxExtendedDaysPerWeek)
                : Texts.ExtendedDaysUsedUp(DrivingLimits.MaxExtendedDaysPerWeek)
        };
        if (quota.CountedIn is { } clock)
            notes.Add(Texts.StrictestExtendedDays(clock));
        if (quota.AcrossMonday)
            notes.Add(Texts.WorkingDayAcrossMonday());
        Record("CE_561_2006.DailyDriving.9hours", "Art. 6.1", Texts.DailyDriving(),
            passed, total.TotalHours, maxToday.TotalHours, note: Texts.Sentences(notes));

        if (!passed)
        {
            _violations.Add(ComplianceViolation.Critical(
                "CE_561_2006.DailyDriving.9hours",
                Texts.DailyDrivingExceeded(total.TotalHours, maxToday.TotalHours)));
        }
    }

    private void EvaluateWeeklyDriving() =>
        EvaluateWeeks("CE_561_2006.WeeklyDriving.56hours", "Art. 6.2", Texts.WeeklyDriving(), 1, DrivingLimits.MaxWeeklyDriving, _week);

    private void EvaluateBiWeeklyDriving() =>
        EvaluateWeeks("CE_561_2006.BiWeeklyDriving.90hours", "Art. 6.3", Texts.TwoWeeksDriving(), 2, DrivingLimits.MaxBiWeeklyDriving, _twoWeeks);

    /// <summary>The strictest of both clocks and of every week, or pair of weeks, the trip touches (SPEC 0064 R1, R2).</summary>
    private void EvaluateWeeks(string ruleId, string article, LocalizedText name, int weeks, TimeSpan limit, WeekLoad load)
    {
        var passed = load.Total <= limit;
        string Monday(int back) => load.LastMonday.AddDays(-7 * back).ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);
        var note = weeks == 1 ? Texts.WeekOf(Monday(0)) : Texts.WeeksOf(Monday(1), Monday(0));
        var countedIn = load.ClocksDiffer ? DrivingWeek.Name(load.Clock) : null;
        if (countedIn is not null)
            note = Texts.StrictestWeek(note, countedIn);
        _checks.Add(new ComplianceRuleCheck(ruleId, article, name, passed ? RuleCheckOutcome.Passed : RuleCheckOutcome.Violated,
            load.Total.TotalHours, limit.TotalHours, RuleUnit.Hours, note)
        {
            CountedIn = countedIn,
            ByClock = (load.InMadrid.TotalHours, load.InUtc.TotalHours)
        });

        if (!passed)
        {
            _violations.Add(ComplianceViolation.Critical(ruleId,
                countedIn is null
                    ? Texts.WeeksExceeded(name, load.Total.TotalHours, limit.TotalHours)
                    : Texts.WeeksExceededInClock(name, load.Total.TotalHours, countedIn, limit.TotalHours)));
        }
    }

    private void EvaluateDailyRest()
    {
        var tripEnd = _tripStart + _tripDuration;
        var days = SplitIntoWorkingDays(MergeIntoBlocks(_fullCalendar, _tripStart, tripEnd));
        var tripDayIndex = days.FindIndex(d => d.Start < tripEnd && d.End > _tripStart);
        if (tripDayIndex < 0)
        {
            Record(DailyRestRuleId, "Art. 8.2", Texts.DailyRest(), true, null, DrivingLimits.DailyRestNormal.TotalHours);
            Record(ReducedRestRuleId, "Art. 8.4", Texts.ReducedDailyRests(), true, null, DrivingLimits.MaxReducedDailyRests, unit: RuleUnit.Rests);
            return;
        }

        var tripDay = days[tripDayIndex];
        var restThatFits = tripDay.Start + DrivingLimits.DailyRestWindow - tripDay.End;
        if (tripDay.ClosingGap is TimeSpan gap && gap < restThatFits)
            restThatFits = gap;
        Record(DailyRestRuleId, "Art. 8.2", Texts.DailyRest(),
            tripDay.Rest != DailyRestKind.Missing,
            Max(TimeSpan.Zero, restThatFits).TotalHours, DrivingLimits.DailyRestNormal.TotalHours,
            note: Texts.WorkingDayRest(tripDay.Start, tripDay.End, tripDay.Rest switch
            {
                DailyRestKind.Regular => Texts.RegularRest(),
                DailyRestKind.Reduced => Texts.ReducedRest(DrivingLimits.DailyRestReduced.TotalHours),
                _ => Texts.InsufficientRest()
            }));

        if (tripDay.Rest == DailyRestKind.Missing)
        {
            _violations.Add(ComplianceViolation.Critical(
                "CE_561_2006.DailyRest.11hours",
                Texts.NoDailyRestFits(tripDay.Start, tripDay.End, DrivingLimits.DailyRestNormal.TotalHours,
                    DrivingLimits.DailyRestReduced.TotalHours, DrivingLimits.DailyRestWindow.TotalHours)));
        }

        // Art. 8.4: at most 3 reduced daily rests between two weekly rests.
        var from = tripDayIndex;
        while (from > 0 && !days[from - 1].EndsWithWeeklyRest)
            from--;
        var to = tripDayIndex;
        while (to < days.Count - 1 && !days[to].EndsWithWeeklyRest)
            to++;

        var reduced = days.Skip(from).Take(to - from + 1).Count(d => d.Rest == DailyRestKind.Reduced);
        Record(ReducedRestRuleId, "Art. 8.4", Texts.ReducedDailyRests(),
            reduced <= DrivingLimits.MaxReducedDailyRests, reduced, DrivingLimits.MaxReducedDailyRests, unit: RuleUnit.Rests);

        if (reduced > DrivingLimits.MaxReducedDailyRests)
        {
            _violations.Add(ComplianceViolation.Critical(
                "CE_561_2006.DailyRest.ReducedLimit",
                Texts.TooManyReducedRests(reduced, DrivingLimits.MaxReducedDailyRests)));
        }
    }

    internal const string DailyRestRuleId = "CE_561_2006.DailyRest.11hours";

    private const string ReducedRestRuleId = "CE_561_2006.DailyRest.ReducedLimit";


    // The compensation of reduced weekly rests (Art. 8.6) is not enforced: DrivingPeriod carries no compensation ledger
    // (docs/REGULATORY_MATRIX.md). With no weekly rest found, its absence is only provable when the known history covers the
    // whole 6×24h window (critical); otherwise it is unverifiable (warning) (SPEC 0040 R2, R3).
    private void EvaluateWeeklyRest()
    {
        const string ruleId = "CE_561_2006.WeeklyRest.45hours";
        var name = Texts.WeeklyRest();
        double? sinceLast = _limits.LastWeeklyRestDate is DateTime last ? (_tripStart - last).TotalHours : null;
        var limit = DrivingLimits.MaxIntervalBetweenWeeklyRests.TotalHours;
        var note = _limits.LastWeeklyRestDate is DateTime lastRest
            ? lastRest == _tripStart
                ? Texts.WeeklyRestUntilDeparture()
                : Texts.LastWeeklyRestEnded(lastRest)
            : Texts.NoWeeklyRestKnown();

        if (_limits.NeedsWeeklyRest(_tripStart))
        {
            if (_limits.LastWeeklyRestDate is null && !HistoryCoversWeeklyRestWindow())
            {
                _checks.Add(new ComplianceRuleCheck(ruleId, "Art. 8.6", name, RuleCheckOutcome.Warning, sinceLast, limit, RuleUnit.Hours,
                    Texts.NotEnoughHistory(note)));
                _violations.Add(ComplianceViolation.Warning(
                    "CE_561_2006.WeeklyRest.Unverifiable",
                    Texts.WeeklyRestUnverifiable()));
                return;
            }

            Record(ruleId, "Art. 8.6", name, false, sinceLast, limit, note: note);
            _violations.Add(ComplianceViolation.Critical(
                "CE_561_2006.WeeklyRest.45hours",
                Texts.WeeklyRestDue(DrivingLimits.WeeklyRestNormal.TotalHours, DrivingLimits.WeeklyRestReduced.TotalHours,
                    DrivingLimits.MaxIntervalBetweenWeeklyRests.TotalHours)));
            return;
        }

        Record(ruleId, "Art. 8.6", name, true, sinceLast, limit, note: note);
    }


    private bool HistoryCoversWeeklyRestWindow() =>
        _orderedHistory.Count > 0
        && _orderedHistory[0].Start <= _tripStart - DrivingLimits.MaxIntervalBetweenWeeklyRests;

    private (TimeSpan Today, TimeSpan ThisWeek, TimeSpan LastTwoWeeks) CalculateRemaining()
    {
        var maxToday = _limits.CanUseExtendedDay()
            ? DrivingLimits.MaxDailyDrivingExtended
            : DrivingLimits.MaxDailyDriving;

        var remainingToday = Max(TimeSpan.Zero, maxToday - _limits.DrivingToday - _tripDuration);
        var remainingThisWeek = Max(TimeSpan.Zero, DrivingLimits.MaxWeeklyDriving - _week.Total);
        var remainingLastTwoWeeks = Max(TimeSpan.Zero, DrivingLimits.MaxBiWeeklyDriving - _twoWeeks.Total);

        return (remainingToday, remainingThisWeek, remainingLastTwoWeeks);
    }

    /// <summary>Art. 7: a gap of 45 minutes before <paramref name="asOf"/> is the mandatory break and stops the count.</summary>
    private static TimeSpan AccumulateContinuousDrivingBefore(IReadOnlyList<DrivingPeriod> history, DateTime asOf)
    {
        var relevant = history.Where(p => p.End <= asOf).ToList();
        var accumulated = TimeSpan.Zero;
        var boundary = asOf;

        for (var i = relevant.Count - 1; i >= 0; i--)
        {
            var period = relevant[i];
            var gapToBoundary = boundary - period.End;

            if (gapToBoundary >= DrivingLimits.MandatoryContinuousBreak)
                break;

            accumulated += period.Duration;
            boundary = period.Start;
        }

        return accumulated;
    }

    /// <summary>
    /// Real driving hours at <paramref name="asOf"/>: only periods already finished count. Without a weekly rest in
    /// <paramref name="history"/>, "since the weekly rest" covers the whole history (SPEC 0047 R1, R3).
    /// </summary>
    public static DriverHours MeasureHours(IReadOnlyList<DrivingPeriod> history, DateTime asOf)
    {
        ArgumentNullException.ThrowIfNull(history);
        var ordered = history.Where(p => p.Start < asOf).OrderBy(p => p.Start).ToList();
        // A service under way ended the rest when it started, even if its driving does not count yet.
        var lastStart = ordered.Where(p => p.End > asOf).Select(p => p.Start).DefaultIfEmpty(asOf).Min();
        var limits = BuildDrivingLimits(ordered, asOf, DayOf(ordered, lastStart, asOf));
        var since = SumDuration(ordered, limits.LastWeeklyRestDate ?? DateTime.MinValue, asOf);
        return DriverHours.Of(limits.DrivingToday, limits.DrivingThisWeek, limits.DrivingLastTwoWeeks, since, limits.LastWeeklyRestDate);
    }

    /// <summary>
    /// Accumulated state before <paramref name="asOf"/>. The weekly figures of a trip depend on the weeks it touches
    /// (<see cref="HeaviestWeeks"/>).
    /// </summary>
    private static DrivingLimits BuildDrivingLimits(IReadOnlyList<DrivingPeriod> history, DateTime asOf, CurrentDay day)
    {
        var drivingThisWeek = HeaviestWeeks(history, asOf, asOf, 1).Total;
        var drivingLastTwoWeeks = HeaviestWeeks(history, asOf, asOf, 2).Total;
        var drivingToday = day.Driven;
        var drivingSinceLastBreak = AccumulateContinuousDrivingBefore(history, asOf);
        var extendedDaysUsedThisWeek = day.Quota.Used;

        var (lastWeeklyRestDate, lastWeeklyRestType) = FindLastWeeklyRest(history, asOf);

        return new DrivingLimits(
            drivingToday,
            drivingThisWeek,
            drivingLastTwoWeeks,
            drivingSinceLastBreak,
            extendedDaysUsedThisWeek,
            reducedDailyRestsUsed: 0,
            lastDailyRestDate: null,
            lastDailyRestType: RestType.None,
            lastWeeklyRestDate: lastWeeklyRestDate,
            lastWeeklyRestType: lastWeeklyRestType);
    }

    /// <summary>Most recent gap between driving periods of at least <see cref="DrivingLimits.WeeklyRestReduced"/>.</summary>
    private static (DateTime? Date, RestType Type) FindLastWeeklyRest(
        IReadOnlyList<DrivingPeriod> history, DateTime asOf)
    {
        // The rest running from the last period that ended before asOf up to asOf is itself a (possibly the most recent) weekly
        // rest, and asOf is when it ends (SPEC 0039).
        var lastEnded = history.LastOrDefault(p => p.End <= asOf);
        if (lastEnded is not null && !history.Any(p => p.Start < asOf && p.End > lastEnded.End))
        {
            var trailingGap = asOf - lastEnded.End;
            if (trailingGap >= DrivingLimits.WeeklyRestReduced)
            {
                return (asOf, trailingGap >= DrivingLimits.WeeklyRestNormal
                    ? RestType.WeeklyNormal
                    : RestType.WeeklyReduced);
            }
        }

        for (var i = history.Count - 1; i >= 1; i--)
        {
            var next = history[i];
            if (next.Start > asOf)
            {
                continue;
            }

            var gap = history[i - 1].GapUntil(next);
            if (gap >= DrivingLimits.WeeklyRestReduced)
            {
                var type = gap >= DrivingLimits.WeeklyRestNormal
                    ? RestType.WeeklyNormal
                    : RestType.WeeklyReduced;
                return (next.Start, type);
            }
        }

        return (null, RestType.None);
    }

    /// <summary>The working day (Art. 4(k)) that holds an instant, and what was driven in it before.</summary>
    private sealed record CurrentDay(DateTime Start, TimeSpan Driven, ExtendedQuota Quota);

    /// <summary>Extended days already used in the week, in the strictest clock (<see cref="CountedIn"/> when they differ).</summary>
    private sealed record ExtendedQuota(int Used, LocalizedText? CountedIn, bool AcrossMonday);

    /// <summary>Driving of one or two consecutive weeks (Art. 4(i)) in the strictest clock and week.</summary>
    private sealed record WeekLoad(TimeSpan Total, WeekClock Clock, DateTime LastMonday, TimeSpan InMadrid, TimeSpan InUtc)
    {
        public bool ClocksDiffer => InMadrid != InUtc;
    }

    /// <summary>
    /// The working day from the end of the last daily rest (a gap of at least <see cref="DrivingLimits.DailyRestReduced"/>), with the
    /// same split as the daily-rest rule, and the extended days before it in the week it starts (SPEC 0064 R3, R4).
    /// </summary>
    private static CurrentDay DayOf(IReadOnlyList<DrivingPeriod> history, DateTime start, DateTime end)
    {
        var finished = history.Where(p => p.End <= start).ToList();
        var days = SplitIntoWorkingDays(MergeIntoBlocks(finished, start, end));
        // The trip is the last block: everything else ended before it.
        var current = days[^1];
        var driven = DrivenIn(finished, current.Start, start);

        var extended = days.SkipLast(1)
            .Where(d => DrivenIn(finished, d.Start, d.End) > DrivingLimits.MaxDailyDriving)
            .ToList();
        var acrossMonday = false;
        var used = DrivingWeek.Clocks.Select(clock =>
        {
            var monday = DrivingWeek.MondayOf(current.Start, clock);
            var week = DrivingWeek.StartUtc(monday, clock);
            acrossMonday |= DrivingWeek.StartUtc(monday.AddDays(7), clock) < end || extended.Any(d => d.Start < week && d.End > week);
            return extended.Count(d => d.Start >= week);
        }).ToList();

        var strictest = used[0] >= used[1] ? 0 : 1;
        return new CurrentDay(current.Start, driven, new ExtendedQuota(used[strictest],
            used[0] == used[1] ? null : DrivingWeek.Name(DrivingWeek.Clocks[strictest]), acrossMonday));
    }

    private static TimeSpan DrivenIn(IReadOnlyList<DrivingPeriod> history, DateTime from, DateTime to) =>
        history.Where(p => p.Start >= from && p.End <= to).Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Duration);

    /// <summary>
    /// Driving of <paramref name="weeks"/> consecutive weeks ending in each one the trip touches, in both clocks. A period across
    /// Monday counts in each week only what falls in it; only finished periods count, plus the trip (SPEC 0064 R1, R2, R5).
    /// </summary>
    private static WeekLoad HeaviestWeeks(IReadOnlyList<DrivingPeriod> history, DateTime start, DateTime end, int weeks)
    {
        WeekLoad? worst = null;
        var perClock = new TimeSpan[DrivingWeek.Clocks.Count];
        foreach (var clock in DrivingWeek.Clocks)
        {
            var first = DrivingWeek.MondayOf(start, clock);
            for (var monday = first; monday == first || DrivingWeek.StartUtc(monday, clock) < end; monday = monday.AddDays(7))
            {
                var from = DrivingWeek.StartUtc(monday.AddDays(7 * (1 - weeks)), clock);
                var to = DrivingWeek.StartUtc(monday.AddDays(7), clock);
                var total = Overlap(start, end, from, to);
                foreach (var period in history)
                {
                    if (period.End <= start) total += Overlap(period.Start, period.End, from, to);
                }

                if (total > perClock[(int)clock]) perClock[(int)clock] = total;
                if (worst is null || total > worst.Total) worst = new WeekLoad(total, clock, monday, default, default);
            }
        }

        return worst! with { InMadrid = perClock[(int)WeekClock.Madrid], InUtc = perClock[(int)WeekClock.Utc] };
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var overlapStart = start > from ? start : from;
        var overlapEnd = end < to ? end : to;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }

    private enum DailyRestKind { Regular, Reduced, Missing }

    private sealed record WorkingDay(DateTime Start, DateTime End, DailyRestKind Rest, bool EndsWithWeeklyRest, TimeSpan? ClosingGap);

    /// <summary>The calendar plus the trip as non-overlapping blocks, so that a re-validated trip is not counted twice.</summary>
    private static List<(DateTime Start, DateTime End)> MergeIntoBlocks(
        IReadOnlyList<DrivingPeriod> calendar, DateTime tripStart, DateTime tripEnd)
    {
        var blocks = new List<(DateTime Start, DateTime End)>();
        var all = calendar.Select(p => (p.Start, p.End)).Append((tripStart, tripEnd)).OrderBy(b => b.Item1);
        foreach (var (start, end) in all)
        {
            if (blocks.Count > 0 && start <= blocks[^1].End)
                blocks[^1] = (blocks[^1].Start, end > blocks[^1].End ? end : blocks[^1].End);
            else
                blocks.Add((start, end));
        }
        return blocks;
    }

    /// <summary>
    /// A working day starts when a rest ends (the first known block is assumed to follow one) and ends at
    /// the first gap of at least <see cref="DrivingLimits.DailyRestReduced"/>. Its closing rest must fit
    /// within <see cref="DrivingLimits.DailyRestWindow"/> of the day's start (Art. 8.2): regular if 11h fit
    /// (or it is a 3h+9h split, Art. 4(g)), reduced if only 9h fit, missing otherwise. The last known day has
    /// no closing rest yet and is judged by the best rest still possible.
    /// </summary>
    private static List<WorkingDay> SplitIntoWorkingDays(List<(DateTime Start, DateTime End)> blocks)
    {
        var days = new List<WorkingDay>();
        var dayStart = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            TimeSpan? closingGap = i + 1 < blocks.Count ? blocks[i + 1].Start - blocks[i].End : null;
            if (closingGap < DrivingLimits.DailyRestReduced)
                continue;

            var start = blocks[dayStart].Start;
            var end = blocks[i].End;
            var hasSplitFirstPart = false;
            for (var j = dayStart; j < i; j++)
                hasSplitFirstPart |= blocks[j + 1].Start - blocks[j].End >= DrivingLimits.SplitDailyRestFirstPart;

            var deadline = start + DrivingLimits.DailyRestWindow;
            var endsWithWeeklyRest = closingGap >= DrivingLimits.WeeklyRestReduced;
            var fitsReduced = end + DrivingLimits.DailyRestReduced <= deadline;
            var fitsRegular = end + DrivingLimits.DailyRestNormal <= deadline
                && (closingGap is null || closingGap >= DrivingLimits.DailyRestNormal);

            var rest = !fitsReduced ? DailyRestKind.Missing
                : fitsRegular || hasSplitFirstPart || endsWithWeeklyRest ? DailyRestKind.Regular
                : DailyRestKind.Reduced;

            days.Add(new WorkingDay(start, end, rest, endsWithWeeklyRest, closingGap));
            dayStart = i + 1;
        }
        return days;
    }

    private static TimeSpan SumDuration(IReadOnlyList<DrivingPeriod> history, DateTime from, DateTime to) =>
        history
            .Where(p => p.Start >= from && p.End <= to)
            .Aggregate(TimeSpan.Zero, (sum, p) => sum + p.Duration);

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;
}
