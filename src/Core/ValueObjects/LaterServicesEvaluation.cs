using System.Globalization;
namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// A CE 561/2006 rule that a later, already assigned service stops meeting, or meets worse, because of a new one: the same
/// rule evaluated with and without the new service (SPEC 0063 R3).
/// </summary>
public sealed record LaterServiceImpact(int? TripId, DateTime Start, ComplianceRuleCheck With, ComplianceRuleCheck? Without)
{
    /// <summary>What follows it is the broken rule of the later service.</summary>
    public const string RuleIdPrefix = "LaterServices.";

    public string RuleId => RuleIdPrefix + With.RuleId;

    /// <summary>For the dispatcher (SPEC 0063 R6).</summary>
    public LocalizedText Message
    {
        get
        {
            var service = TripId?.ToString(CultureInfo.InvariantCulture) ?? "?";
            var sameLimit = Without is null || Without.Limit == With.Limit;
            return sameLimit
                ? Texts.LaterServiceSameLimit(service, Start, With.Article, With.Name, Figure(With), Figure(Without), Limit(With))
                : Texts.LaterServiceOtherLimit(service, Start, With.Article, With.Name, Figure(With), Limit(With), Figure(Without), Limit(Without!));
        }
    }

    /// <summary>The daily rest fails below the reduced one, not below the regular one.</summary>
    private static LocalizedText Limit(ComplianceRuleCheck check) => LaterServicesEvaluation.IsRestThatFits(check)
        ? Texts.AtLeast(DrivingLimits.DailyRestReduced.TotalHours)
        : Texts.AtMost(check.Limit is double limit ? Amount(limit, check.Unit) : Texts.NoLimit());

    private static LocalizedText Figure(ComplianceRuleCheck? check) => check switch
    {
        null => Texts.NotEvaluatedFigure(),
        { Actual: double actual, CountedIn: { } clock } => Texts.AmountInClock(Amount(actual, check.Unit), clock),
        { Actual: double actual } => Amount(actual, check.Unit),
        _ => Texts.NoneInHistory()
    };

    private static LocalizedText Amount(double value, RuleUnit unit) =>
        unit == RuleUnit.Rests ? Texts.AmountOfRests(value) : Texts.AmountOfHours(value);
}

/// <summary>
/// The later services evaluated, up to when, and the rules they would break because of the new one. <see cref="Results"/> keeps
/// the evaluation of each affected service with the new one included.
/// </summary>
public sealed record LaterServicesCheck(
    int Evaluated,
    DateTime Until,
    IReadOnlyList<LaterServiceImpact> Impacts,
    IReadOnlyDictionary<int, ComplianceCheckResult> Results)
{
    public bool HasImpacts => Impacts.Count > 0;

    /// <summary>By departure.</summary>
    public IReadOnlyList<int> AffectedTripIds => Impacts.Select(i => i.TripId).OfType<int>().Distinct().ToList();
}

/// <summary>
/// A driver's later, already assigned services must stay legal when a new one is added (SPEC 0063, ADR 0016). Pure: it re-runs
/// <see cref="LegalComplianceEvaluation.Validate"/> for each later service without and with the new one, so there are
/// no extra rules or figures. Only what the new service breaks or makes worse counts (R3).
/// </summary>
public static class LaterServicesEvaluation
{
    private const double Epsilon = 1e-9;
    private static readonly IReadOnlyDictionary<int, ComplianceCheckResult> NoResults = new Dictionary<int, ComplianceCheckResult>();

    /// <param name="calendar">Every known period of the driver around the trip. It may already hold the trip itself (same driver again).</param>
    /// <param name="trip">The service being assigned.</param>
    public static LaterServicesCheck Evaluate(IReadOnlyList<DrivingPeriod> calendar, DrivingPeriod trip)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(trip);

        var until = trip.End + LegalComplianceEvaluation.LookbackWindow;
        // R2: every service whose own lookback sees the trip. One that overlaps it belongs to the schedule check.
        var later = calendar
            .Where(p => !IsSame(p, trip) && p.Start >= trip.End && p.Start < until)
            .OrderBy(p => p.Start).ThenBy(p => p.TripId)
            .ToList();

        // R4: the same driver again does not change the calendar, so nothing can get worse.
        if (later.Count == 0 || calendar.Any(p => IsSame(p, trip)))
        {
            return new LaterServicesCheck(later.Count, until, [], NoResults);
        }

        var impacts = new List<LaterServiceImpact>();
        var results = new Dictionary<int, ComplianceCheckResult>();
        foreach (var service in later)
        {
            var others = Around(calendar.Where(p => !ReferenceEquals(p, service)), service);
            var without = LegalComplianceEvaluation.Validate(others, service.Start, service.Duration);
            var with = LegalComplianceEvaluation.Validate([.. others, trip], service.Start, service.Duration);

            var before = impacts.Count;
            foreach (var check in with.RuleChecks.Where(c => c.Outcome == RuleCheckOutcome.Violated))
            {
                var previous = without.RuleChecks.FirstOrDefault(c => c.RuleId == check.RuleId);
                if (Worsened(check, previous) is { } worse)
                {
                    impacts.Add(new LaterServiceImpact(service.TripId, service.Start, worse.With, worse.Without));
                }
            }

            if (impacts.Count > before && service.TripId is int id)
            {
                results[id] = with;
            }
        }

        return new LaterServicesCheck(later.Count, until, impacts, results);
    }

    /// <summary>Figure = rest that fits: the lower, the worse. Every other rule gets worse upwards.</summary>
    internal static bool IsRestThatFits(ComplianceRuleCheck check) => check.RuleId == LegalComplianceEvaluation.DailyRestRuleId;

    /// <summary>
    /// R3 on the figure shown and, for weeks, on each clock (SPEC 0064 R1: both must be met): an already illegal figure in one
    /// clock must not hide that the other one stops complying. Null when nothing got worse.
    /// </summary>
    private static (ComplianceRuleCheck With, ComplianceRuleCheck? Without)? Worsened(ComplianceRuleCheck with, ComplianceRuleCheck? without)
    {
        if (IsWorse(with, without))
        {
            return (with, without);
        }

        foreach (var clock in DrivingWeek.Clocks)
        {
            if (In(clock, with) is { Outcome: RuleCheckOutcome.Violated } withInClock
                && In(clock, without) is { } withoutInClock
                && IsWorse(withInClock, withoutInClock))
            {
                return (withInClock, withoutInClock);
            }
        }

        return null;
    }

    /// <summary>As that clock alone counts it; null when the rule does not count weeks.</summary>
    private static ComplianceRuleCheck? In(WeekClock clock, ComplianceRuleCheck? check)
    {
        if (check?.ByClock is not { } both || check.Limit is not double limit)
        {
            return null;
        }

        var actual = clock == WeekClock.Utc ? both.Utc : both.Madrid;
        return check with
        {
            Actual = actual,
            CountedIn = DrivingWeek.Name(clock),
            Outcome = actual > limit + Epsilon ? RuleCheckOutcome.Violated : RuleCheckOutcome.Passed
        };
    }

    /// <summary>R3: newly violated, or already violated and now further from its limit.</summary>
    private static bool IsWorse(ComplianceRuleCheck with, ComplianceRuleCheck? without)
    {
        if (without is null || without.Outcome != RuleCheckOutcome.Violated)
        {
            return true;
        }

        return (with.Actual, without.Actual) switch
        {
            (null, double) => true, // A weekly rest existed and no longer does.
            (_, null) => false,
            (double restWith, double restWithout) when IsRestThatFits(with) => restWith < restWithout - Epsilon,
            (double drivenWith, double drivenWithout) => drivenWith - (with.Limit ?? 0) > drivenWithout - (without.Limit ?? 0) + Epsilon
        };
    }

    /// <summary>The same window the service sees when it is assigned (<c>LegalValidationService</c>).</summary>
    private static List<DrivingPeriod> Around(IEnumerable<DrivingPeriod> calendar, DrivingPeriod service)
    {
        var from = service.Start - LegalComplianceEvaluation.LookbackWindow;
        var to = service.End + LegalComplianceEvaluation.LookaheadWindow;
        return calendar.Where(p => p.Start < to && p.End > from).ToList();
    }

    private static bool IsSame(DrivingPeriod one, DrivingPeriod other) => one.TripId is int id && id == other.TripId;
}
