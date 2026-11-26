using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Exceptions;

/// <summary>A state conflict: the agenda entry is already over, so it is history (SPEC 0078 R3).</summary>
public sealed class UnavailabilityFinishedException(DateTime end) : Exception(Texts.AgendaEntryFinished(end).ToString()), IHasLocalizedText
{
    public LocalizedText Text { get; } = Texts.AgendaEntryFinished(end);
}
