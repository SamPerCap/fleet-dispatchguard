using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Exceptions;

public sealed class ScheduleConflictException : Exception, IHasLocalizedText
{
    public DecisionExplanation? Explanation { get; init; }

    public IReadOnlyList<int> ConflictingTripIds { get; }

    public LocalizedText Text => TextOf(ConflictingTripIds);

    public ScheduleConflictException(IEnumerable<int> conflictingTripIds)
        : this(conflictingTripIds.ToList())
    {
    }

    private ScheduleConflictException(List<int> conflictingTripIds)
        : base(TextOf(conflictingTripIds).ToString()) => ConflictingTripIds = conflictingTripIds.AsReadOnly();

    private static LocalizedText TextOf(IReadOnlyList<int> tripIds) => tripIds.Count == 0
        ? Texts.OverlapsAnotherService()
        : Texts.OverlapsServices(string.Join(", ", tripIds.Select(id => $"#{id}")));
}
