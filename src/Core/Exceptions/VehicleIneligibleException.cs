using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Exceptions;

public sealed class VehicleIneligibleException : Exception, IHasLocalizedText
{
    public DecisionExplanation? Explanation { get; init; }

    public IReadOnlyCollection<LocalizedText> Violations { get; }

    public LocalizedText Text { get; }

    public VehicleIneligibleException(IEnumerable<LocalizedText> violations)
        : this(violations.ToList())
    {
    }

    private VehicleIneligibleException(List<LocalizedText> violations)
        : this(violations, violations.Count == 0 ? Texts.VehicleNotFit() : Texts.Listed(violations))
    {
    }

    private VehicleIneligibleException(List<LocalizedText> violations, LocalizedText text)
        : base(text.ToString())
    {
        Violations = violations.AsReadOnly();
        Text = text;
    }
}
