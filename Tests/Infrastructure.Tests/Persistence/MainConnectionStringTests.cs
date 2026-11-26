using DispatchGuard.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Tests.Persistence;

/// <summary>A release and the version before it share the settings of the host (SPEC 0094 R3).</summary>
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
    public void Read_OnlyThePreviousName_ReturnsIt() => Assert.Equal("old", Read((Previous, "old")));

    [Fact]
    public void Read_BothNames_TheNewOneWins() => Assert.Equal("new", Read((Previous, "old"), (Current, "new")));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Read_TheNewNameIsBlank_ReturnsThePreviousOne(string blank) => Assert.Equal("old", Read((Current, blank), (Previous, "old")));

    [Fact]
    public void Read_Neither_IsNull() => Assert.Null(Read());
}
