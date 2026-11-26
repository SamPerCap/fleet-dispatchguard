using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class ScheduleConflictCheckTests
{
    [Fact]
    public void NoConflict_ProducesHasConflictFalse_WithEmptyConflictingTripIds()
    {
        var check = ScheduleConflictCheck.NoConflict();

        Assert.False(check.HasConflict);
        Assert.Empty(check.ConflictingTripIds);
    }

    [Fact]
    public void Conflict_WithNonEmptyIds_ProducesHasConflictTrue_AndSameIds()
    {
        var conflictingTripIds = new[] { 10, 11 };

        var check = ScheduleConflictCheck.Conflict(conflictingTripIds);

        Assert.True(check.HasConflict);
        Assert.Equal(2, check.ConflictingTripIds.Count);
        Assert.Equal(conflictingTripIds, check.ConflictingTripIds);
    }

    [Fact]
    public void Conflict_WithSingleId_ContainsThatId()
    {
        var check = ScheduleConflictCheck.Conflict(new[] { 42 });

        Assert.True(check.HasConflict);
        var single = Assert.Single(check.ConflictingTripIds);
        Assert.Equal(42, single);
    }

    [Fact]
    public void Conflict_WithEmptyCollection_ThrowsArgumentException()
    {
        var emptyIds = Array.Empty<int>();

        Assert.ThrowsAny<ArgumentException>(() => ScheduleConflictCheck.Conflict(emptyIds));
    }

    [Fact]
    public void Conflict_WithNullCollection_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => ScheduleConflictCheck.Conflict(null!));
    }

    [Fact]
    public void NoConflict_CheckedAt_IsStampedNearUtcNow()
    {
        var before = DateTime.UtcNow;

        var check = ScheduleConflictCheck.NoConflict();
        var after = DateTime.UtcNow;

        Assert.InRange(check.CheckedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void Conflict_CheckedAt_IsStampedNearUtcNow()
    {
        var before = DateTime.UtcNow;

        var check = ScheduleConflictCheck.Conflict(new[] { 1 });
        var after = DateTime.UtcNow;

        Assert.InRange(check.CheckedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
