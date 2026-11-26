using DispatchGuard.API.Http;

namespace Infrastructure.Tests.Http;

public class UtcInstantExtensionsTests
{
    private static readonly DateTime TenOClock = new(2026, 11, 23, 10, 0, 0);

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AsUtc_UtcOrWithoutZone_KeepsTheClockTimeAsUtc(DateTimeKind kind)
    {
        var instant = DateTime.SpecifyKind(TenOClock, kind).AsUtc();

        Assert.Equal(TenOClock, instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Fact]
    public void AsUtc_LocalTime_IsConverted()
    {
        var local = DateTime.SpecifyKind(TenOClock, DateTimeKind.Utc).ToLocalTime();

        var instant = local.AsUtc();

        Assert.Equal(TenOClock, instant);
        Assert.Equal(DateTimeKind.Utc, instant.Kind);
    }

    [Fact]
    public void AsUtc_Null_StaysNull()
    {
        DateTime? missing = null;

        Assert.Null(missing.AsUtc());
    }

    [Fact]
    public void AsUtc_NullableWithValue_IsConvertedToo()
    {
        DateTime? withoutZone = TenOClock;

        Assert.Equal(DateTimeKind.Utc, withoutZone.AsUtc()!.Value.Kind);
    }
}
