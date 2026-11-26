using DispatchGuard.Core.ValueObjects;
using Xunit;

namespace Core.Tests.ValueObjects;

public class AllocationOverrideTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutReason_Throws(string? reason) =>
        Assert.ThrowsAny<ArgumentException>(() => AllocationOverride.Create(reason, "Lucía"));

    [Fact]
    public void Create_ReasonOneCharTooShortAfterTrim_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => AllocationOverride.Create("  " + new string('a', AllocationOverride.MinReasonLength - 1) + "  ", "Lucía"));

    [Fact]
    public void Create_ReasonAtMinimum_TrimsAndAccepts()
    {
        var ov = AllocationOverride.Create("  " + new string('a', AllocationOverride.MinReasonLength) + " ", " Lucía ");
        Assert.Equal(AllocationOverride.MinReasonLength, ov.Reason.Length);
        Assert.Equal("Lucía", ov.By);
    }

    [Fact]
    public void Create_ReasonOverMaximum_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => AllocationOverride.Create(new string('a', AllocationOverride.MaxReasonLength + 1), "Lucía"));

    [Fact]
    public void Create_ReasonAtMaximum_Accepts() =>
        Assert.Equal(AllocationOverride.MaxReasonLength, AllocationOverride.Create(new string('a', AllocationOverride.MaxReasonLength), "Lucía").Reason.Length);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_WithoutWho_Throws(string? by) =>
        Assert.ThrowsAny<ArgumentException>(() => AllocationOverride.Create("Sin otro conductor disponible", by));
}
