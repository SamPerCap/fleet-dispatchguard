namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Explicit human decision to force an allocation rejected for CE 561/2006. Only compliance can be forced; vehicle eligibility
/// and schedule conflicts never (SPEC 0056 R1, ADR 0015).
/// </summary>
public sealed record AllocationOverride
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;
    public const int MaxByLength = 100;

    public string Reason { get; }
    public string By { get; }

    private AllocationOverride(string reason, string by)
    {
        Reason = reason;
        By = by;
    }

    /// <exception cref="ArgumentException">Missing or out-of-range reason, or missing name: both are required (SPEC 0056 R2).</exception>
    public static AllocationOverride Create(string? reason, string? by)
    {
        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length < MinReasonLength || trimmedReason.Length > MaxReasonLength)
            throw new InvalidRequestException(Texts.OverrideReasonLength(MinReasonLength, MaxReasonLength));
        var trimmedBy = by?.Trim() ?? string.Empty;
        if (trimmedBy.Length == 0 || trimmedBy.Length > MaxByLength)
            throw new InvalidRequestException(Texts.OverrideByMissing(MaxByLength));
        return new AllocationOverride(trimmedReason, trimmedBy);
    }
}
