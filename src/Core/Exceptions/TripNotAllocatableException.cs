namespace DispatchGuard.Core.Exceptions;

/// <summary>A state conflict (cancelled or locked trip), not a rule rejection (SPEC 0035 R4).</summary>
public sealed class TripNotAllocatableException(LocalizedText text) : Exception(text.ToString()), IHasLocalizedText
{
    public LocalizedText Text { get; } = text;
}
