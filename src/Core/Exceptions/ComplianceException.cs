using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Exceptions;

public sealed class ComplianceException : Exception, IHasLocalizedText
{
    public DecisionExplanation? Explanation { get; init; }

    /// <summary>Already assigned services of the driver that the rejected one would make illegal (SPEC 0063 R7); empty if none.</summary>
    public IReadOnlyList<LaterServiceImpact> LaterServices { get; init; } = [];

    public IReadOnlyCollection<LocalizedText> Violations { get; }

    public LocalizedText Text { get; }

    public ComplianceException(IEnumerable<LocalizedText> violations)
        : this(violations.ToList())
    {
    }

    private ComplianceException(List<LocalizedText> violations)
        : this(violations, violations.Count == 0 ? Texts.DriverNotCompliant() : Texts.Listed(violations))
    {
    }

    private ComplianceException(List<LocalizedText> violations, LocalizedText text)
        : base(text.ToString())
    {
        Violations = violations.AsReadOnly();
        Text = text;
    }
}
