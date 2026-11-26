namespace DispatchGuard.Core.ValueObjects;

public enum RuleCheckOutcome
{
    Passed = 0,
    Warning = 1,
    Violated = 2
}

/// <summary>What <see cref="ComplianceRuleCheck.Actual"/> and <see cref="ComplianceRuleCheck.Limit"/> count.</summary>
public enum RuleUnit
{
    Hours = 0,
    Rests = 1
}

/// <summary>
/// One CE 561/2006 rule as evaluated, passed or not: the figure projected with the trip (<see cref="Actual"/>) against the
/// limit applied (<see cref="Limit"/>, always taken from <see cref="DrivingLimits"/>). Explanations are built from these
/// values, never from hard-coded text (SPEC 0042). <see cref="Note"/> carries computed context, e.g. the working day.
/// </summary>
public sealed record ComplianceRuleCheck(
    string RuleId,
    string Article,
    LocalizedText Name,
    RuleCheckOutcome Outcome,
    double? Actual,
    double? Limit,
    RuleUnit Unit,
    LocalizedText? Note = null)
{
    /// <summary>
    /// Clock of <see cref="Actual"/> when the week counted in Madrid time and in UTC gives different figures (SPEC 0064 R6):
    /// the strictest one is shown. Null when both give the same.
    /// </summary>
    public LocalizedText? CountedIn { get; init; }

    /// <summary>Figure of each clock (<see cref="DrivingWeek.Clocks"/>) when the rule counts weeks; both must be met on their own.</summary>
    internal (double Madrid, double Utc)? ByClock { get; init; }
}
