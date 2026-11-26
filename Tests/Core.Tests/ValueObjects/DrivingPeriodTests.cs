using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>The input of the engine: a period must move forward in time, and equality ignores the trip (SPEC 0063).</summary>
public class DrivingPeriodTests
{
    private static readonly DateTime Start = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void New_EndNotAfterStart_IsRejected(int minutes) =>
        Assert.Throws<ArgumentException>("end", () => new DrivingPeriod(1, Start, Start.AddMinutes(minutes)));

    [Fact]
    public void Equals_SameDriverStartAndEnd_IsEqualWhateverTheTrip() =>
        Assert.True(new DrivingPeriod(1, Start, Start.AddHours(2), 10).Equals(new DrivingPeriod(1, Start, Start.AddHours(2), 20)));

    [Theory]
    [InlineData(2, 0, 2)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 0, 3)]
    public void Equals_AnotherDriverStartOrEnd_IsNotEqual(int driverId, int startHour, int endHour) =>
        Assert.False(new DrivingPeriod(1, Start, Start.AddHours(2)).Equals(new DrivingPeriod(driverId, Start.AddHours(startHour), Start.AddHours(endHour))));

    [Fact]
    public void Equals_Null_IsNotEqual() => Assert.False(new DrivingPeriod(1, Start, Start.AddHours(2)).Equals(null));
}
