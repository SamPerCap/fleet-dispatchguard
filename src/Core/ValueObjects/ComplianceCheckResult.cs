namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Full result of the CE 561/2006 engine. <see cref="LegalValidation"/> is the simpler snapshot persisted on the trip.
/// </summary>
public sealed class ComplianceCheckResult
{
    public bool IsCompliant { get; }

    public IReadOnlyCollection<ComplianceViolation> Violations { get; }


    /// <summary>Never negative.</summary>
    public TimeSpan RemainingToday { get; }

    /// <summary>Never negative.</summary>
    public TimeSpan RemainingThisWeek { get; }

    /// <summary>In the two consecutive natural weeks of Art. 6.3 (SPEC 0064). Never negative.</summary>
    public TimeSpan RemainingLastTwoWeeks { get; }

    /// <summary>
    /// Every rule as evaluated, passed or not, with its computed figures. Empty when the result was not produced by
    /// <see cref="LegalComplianceEvaluation"/> (e.g. test fakes).
    /// </summary>
    public IReadOnlyList<ComplianceRuleCheck> RuleChecks { get; private set; } = Array.Empty<ComplianceRuleCheck>();

    internal ComplianceCheckResult WithRuleChecks(IEnumerable<ComplianceRuleCheck> checks)
    {
        RuleChecks = checks.ToList().AsReadOnly();
        return this;
    }

    /// <summary>Null when the driver's later services were not evaluated (SPEC 0063).</summary>
    public LaterServicesCheck? LaterServices { get; private set; }

    /// <summary>
    /// Each rule the later services would break is a critical violation, whatever the trip's own verdict (SPEC 0063 R5). Rule checks
    /// stay those of the trip itself.
    /// </summary>
    public ComplianceCheckResult WithLaterServices(LaterServicesCheck later)
    {
        ArgumentNullException.ThrowIfNull(later);
        var violations = Violations.Concat(later.Impacts.Select(i => ComplianceViolation.Critical(i.RuleId, i.Message))).ToList();
        return new ComplianceCheckResult(IsCompliant && !later.HasImpacts, violations, RemainingToday, RemainingThisWeek, RemainingLastTwoWeeks)
        {
            RuleChecks = RuleChecks,
            LaterServices = later
        };
    }

    public ComplianceCheckResult(
        bool isCompliant,
        IEnumerable<ComplianceViolation> violations,
        TimeSpan remainingToday,
        TimeSpan remainingThisWeek,
        TimeSpan remainingLastTwoWeeks)
    {
        IsCompliant = isCompliant;
        Violations = (violations?.ToList() ?? new List<ComplianceViolation>()).AsReadOnly();

        RemainingToday = remainingToday;
        RemainingThisWeek = remainingThisWeek;
        RemainingLastTwoWeeks = remainingLastTwoWeeks;
    }

    public static ComplianceCheckResult Compliant(
        TimeSpan remainingToday,
        TimeSpan remainingThisWeek,
        TimeSpan remainingLastTwoWeeks) =>
        new(
            isCompliant: true,
            violations: Array.Empty<ComplianceViolation>(),

            remainingToday,
            remainingThisWeek,
            remainingLastTwoWeeks);

    /// <summary>Compliant although it carries non-blocking warnings (SPEC 0040).</summary>
    public static ComplianceCheckResult CompliantWithWarnings(
        IEnumerable<ComplianceViolation> warnings,
        TimeSpan remainingToday,
        TimeSpan remainingThisWeek,
        TimeSpan remainingLastTwoWeeks) =>
        new(
            isCompliant: true,
            violations: warnings.ToList(),

            remainingToday,
            remainingThisWeek,
            remainingLastTwoWeeks);

    public static ComplianceCheckResult NonCompliant(
        IEnumerable<ComplianceViolation> violations,
        TimeSpan remainingToday,
        TimeSpan remainingThisWeek,
        TimeSpan remainingLastTwoWeeks) =>
        new(
            isCompliant: false,
            violations: violations?.ToList() ?? [],
            remainingToday,
            remainingThisWeek,
            remainingLastTwoWeeks);

    /// <summary>For the trip: only the messages, split by severity.</summary>
    public LegalValidation ToSnapshot() =>
        IsCompliant
            ? LegalValidation.CompliantWithWarnings(
                Violations
                    .Where(v => v.Severity == Enums.ViolationSeverity.Warning)
                    .Select(v => v.Message)
                    .ToArray())
            : LegalValidation.NonCompliant(
                Violations
                    .Where(v => v.Severity == Enums.ViolationSeverity.Critical)
                    .Select(v => v.Message)
                    .ToArray());
}
