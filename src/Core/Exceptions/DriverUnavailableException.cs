using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Exceptions;

/// <summary>
/// Inactive or with an unavailability during the service (SPEC 0066). Never forcible: forcing only skips CE 561/2006 (ADR 0015).
/// </summary>
public sealed class DriverUnavailableException : Exception, IHasLocalizedText
{
    public DecisionExplanation? Explanation { get; init; }

    public IReadOnlyCollection<LocalizedText> Violations { get; }

    public LocalizedText Text => Texts.Listed(Violations.ToList());

    public DriverUnavailableException(IEnumerable<LocalizedText> violations)
        : this(violations.ToList())
    {
    }

    private DriverUnavailableException(List<LocalizedText> violations)
        : base(Texts.Listed(violations).ToString()) => Violations = violations.AsReadOnly();
}
