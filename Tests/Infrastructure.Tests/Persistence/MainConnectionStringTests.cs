using DispatchGuard.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Tests.Persistence;

/// <summary>The name before the rename is no longer read: its compatibility lasted one release (SPEC 0094 R3, removed in 0099).</summary>
public class MainConnectionStringTests
{
    private const string Current = "ConnectionStrings:DispatchGuardDb";
    private const string Previous = "ConnectionStrings:BetterBusDb";

    private static string? Read(params (string Key, string Value)[] settings) =>
        MainConnectionString.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value)).Build());

    [Fact]
    public void Read_OnlyTheNewName_ReturnsIt() => Assert.Equal("new", Read((Current, "new")));

    [Fact]
    public void Read_OnlyThePreviousName_IsNull() => Assert.Null(Read((Previous, "old")));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Read_TheNewNameIsBlank_IsNull(string blank) => Assert.Null(Read((Current, blank), (Previous, "old")));

    [Fact]
    public void Read_Neither_IsNull() => Assert.Null(Read());
}
