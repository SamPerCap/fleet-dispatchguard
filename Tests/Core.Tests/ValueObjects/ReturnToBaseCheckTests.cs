using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class ReturnToBaseCheckTests
{
    [Fact]
    public void Compliant_ValidInputs_ProducesCompliantStatusWithCorrectValues()
    {
        var lastReturnDate = new DateTime(2026, 6, 15);
        const int daysSinceLastReturn = 18;

        var result = ReturnToBaseCheck.Compliant(lastReturnDate, daysSinceLastReturn);

        Assert.Equal(ReturnToBaseStatus.Compliant, result.Status);
        Assert.Equal(lastReturnDate, result.LastReturnDate);
        Assert.Equal(daysSinceLastReturn, result.DaysSinceLastReturn);
        Assert.True(result.IsCompliant);
    }

    [Fact]
    public void Compliant_NegativeDaysSinceLastReturn_ThrowsArgumentOutOfRangeException()
    {
        var lastReturnDate = new DateTime(2026, 6, 15);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => ReturnToBaseCheck.Compliant(lastReturnDate, -1));
        Assert.Equal("daysSinceLastReturn", ex.ParamName);
    }

    [Fact]
    public void NonCompliant_BothNull_ProducesNonCompliantStatusWithNullValues()
    {
        var result = ReturnToBaseCheck.NonCompliant(null, null);

        Assert.Equal(ReturnToBaseStatus.NonCompliant, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public void NonCompliant_BothProvidedAndCoherent_ProducesCorrectValues()
    {
        var lastReturnDate = new DateTime(2026, 5, 1);
        const int daysSinceLastReturn = 40;

        var result = ReturnToBaseCheck.NonCompliant(lastReturnDate, daysSinceLastReturn);

        Assert.Equal(ReturnToBaseStatus.NonCompliant, result.Status);
        Assert.Equal(lastReturnDate, result.LastReturnDate);
        Assert.Equal(daysSinceLastReturn, result.DaysSinceLastReturn);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public void NonCompliant_DateProvidedButDaysNull_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ReturnToBaseCheck.NonCompliant(new DateTime(2026, 5, 1), null));
    }

    [Fact]
    public void NonCompliant_DaysProvidedButDateNull_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ReturnToBaseCheck.NonCompliant(null, 10));
    }

    [Fact]
    public void NotVerifiable_ProducesNotVerifiableStatusWithNullValues()
    {
        var result = ReturnToBaseCheck.NotVerifiable();

        Assert.Equal(ReturnToBaseStatus.NotVerifiable, result.Status);
        Assert.Null(result.LastReturnDate);
        Assert.Null(result.DaysSinceLastReturn);
        Assert.False(result.IsCompliant);
    }
}
