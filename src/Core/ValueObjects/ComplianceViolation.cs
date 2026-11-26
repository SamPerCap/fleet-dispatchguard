using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.ValueObjects;

public sealed class ComplianceViolation
{
    /// <summary>E.g. "CE_561_2006.ContinuousDriving.4_5hours".</summary>
    public string RuleId { get; }

    /// <summary>Critical blocks the allocation; a warning is only recorded.</summary>
    public ViolationSeverity Severity { get; }

    public LocalizedText Message { get; }

    public ComplianceViolation(string ruleId, ViolationSeverity severity, LocalizedText message)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Severity = severity;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public static ComplianceViolation Critical(string ruleId, LocalizedText message) =>
        new(ruleId, ViolationSeverity.Critical, message);

    public static ComplianceViolation Warning(string ruleId, LocalizedText message) =>
        new(ruleId, ViolationSeverity.Warning, message);

    public override string ToString() => $"{RuleId}: {Message}";
}
