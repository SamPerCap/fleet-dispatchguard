namespace DispatchGuard.Core.ValueObjects;

public sealed class LegalValidation
{
    public bool IsCompliant { get; }
    public IReadOnlyCollection<LocalizedText> Violations { get; } = null!;
    public IReadOnlyCollection<LocalizedText> Warnings { get; } = null!;
    public DateTime ValidatedAt { get; }

    // Only for EF Core.
    public LegalValidation()
    {
    
    }

    public LegalValidation(
        bool isCompliant,
        IEnumerable<LocalizedText> violations,
        IEnumerable<LocalizedText> warnings)
    {
        IsCompliant = isCompliant;
        Violations = violations.ToList().AsReadOnly();
        Warnings = warnings.ToList().AsReadOnly();
        ValidatedAt = DateTime.UtcNow;
    }

    public static LegalValidation Compliant() =>
        new LegalValidation(true, [], []);

    public static LegalValidation NonCompliant(params LocalizedText[] violations) =>
        new LegalValidation(false, violations, []);

    public static LegalValidation CompliantWithWarnings(params LocalizedText[] warnings) =>
        new LegalValidation(true, [], warnings);
}