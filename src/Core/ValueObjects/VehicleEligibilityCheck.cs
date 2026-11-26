namespace DispatchGuard.Core.ValueObjects;

public sealed class VehicleEligibilityCheck
{
    public bool IsEligible { get; }

    public IReadOnlyCollection<ComplianceViolation> Violations { get; }

    public DateTime CheckedAt { get; }

    private VehicleEligibilityCheck(
        bool isEligible,
        IEnumerable<ComplianceViolation> violations,
        DateTime checkedAt)
    {
        IsEligible = isEligible;
        Violations = violations.ToList().AsReadOnly();
        CheckedAt = checkedAt;
    }

    public static VehicleEligibilityCheck Eligible() =>
        new(true, Array.Empty<ComplianceViolation>(), DateTime.UtcNow);

    /// <exception cref="ArgumentException">An ineligible result must always carry a reason.</exception>
    public static VehicleEligibilityCheck Ineligible(IEnumerable<ComplianceViolation> violations)
    {
        var list = violations?.ToList() ?? new List<ComplianceViolation>();
        if (list.Count == 0)
        {
            throw new ArgumentException("An ineligible result must carry at least one violation.", nameof(violations));
        }

        return new VehicleEligibilityCheck(false, list, DateTime.UtcNow);
    }
}
