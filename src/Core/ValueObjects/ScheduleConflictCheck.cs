namespace DispatchGuard.Core.ValueObjects;

public sealed class ScheduleConflictCheck
{
    public bool HasConflict { get; }

    public IReadOnlyList<int> ConflictingTripIds { get; }

    public DateTime CheckedAt { get; }

    private ScheduleConflictCheck(
        bool hasConflict,
        IEnumerable<int> conflictingTripIds,
        DateTime checkedAt)
    {
        HasConflict = hasConflict;
        ConflictingTripIds = conflictingTripIds.ToList().AsReadOnly();
        CheckedAt = checkedAt;
    }

    public static ScheduleConflictCheck NoConflict() =>
        new(false, Array.Empty<int>(), DateTime.UtcNow);

    /// <exception cref="ArgumentException">A conflict must carry at least one conflicting trip.</exception>
    public static ScheduleConflictCheck Conflict(IEnumerable<int> conflictingTripIds)
    {
        var list = conflictingTripIds?.ToList() ?? new List<int>();
        if (list.Count == 0)
        {
            throw new ArgumentException("A conflict result must carry at least one conflicting trip id.", nameof(conflictingTripIds));
        }

        return new ScheduleConflictCheck(true, list, DateTime.UtcNow);
    }
}
