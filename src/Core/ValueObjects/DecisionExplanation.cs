namespace DispatchGuard.Core.ValueObjects;

public enum ExplanationOutcome
{
    Passed = 0,
    Warning = 1,
    Failed = 2,

    /// <summary>An earlier step failed and the engine is fail-fast (ADR 0001).</summary>
    NotEvaluated = 3
}

/// <summary>For the dispatcher: a rule or criterion with the figures the engine computed.</summary>
public sealed record ExplanationItem(LocalizedText Title, string? Reference, ExplanationOutcome Outcome, LocalizedText Detail)
{
    /// <summary>What the item is, for the code of the web: the title changes with the language.</summary>
    public string Code => Title.Key;
}

/// <summary>For developers: a code component, its role and what it returned.</summary>
public sealed record TechnicalStep(string Component, LocalizedText Role, ExplanationOutcome Outcome, LocalizedText Result);

/// <summary><see cref="Adrs"/> are repository-relative paths to the ADRs behind the decision.</summary>
public sealed record DecisionExplanation(
    LocalizedText Summary,
    IReadOnlyList<ExplanationItem> Business,
    IReadOnlyList<TechnicalStep> Flow,
    IReadOnlyList<LocalizedText> Patterns,
    IReadOnlyList<string> Adrs);
