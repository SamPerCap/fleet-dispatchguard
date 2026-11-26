using DispatchGuard.Core.Entities;

namespace Core.Tests.Entities;

public class ClientTests
{
    private static Client NewClient() => new("Original name");

    [Fact]
    public void Rename_ValidName_IsTrimmedAndStored()
    {
        var client = NewClient();

        client.Rename("  New name  ");

        Assert.Equal("New name", client.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_WithoutName_IsRejectedAndKeepsTheOriginal(string? name)
    {
        var client = NewClient();

        Assert.ThrowsAny<ArgumentException>(() => client.Rename(name!));
        Assert.Equal("Original name", client.Name);
    }

    [Fact]
    public void Rename_LongestAllowedName_IsStored()
    {
        var client = NewClient();
        var longestName = new string('x', Client.MaxNameLength);

        client.Rename(longestName);

        Assert.Equal(longestName, client.Name);
    }

    [Fact]
    public void Rename_OneCharacterTooLong_IsRejected()
    {
        var tooLong = new string('x', Client.MaxNameLength + 1);

        Assert.ThrowsAny<ArgumentException>(() => NewClient().Rename(tooLong));
    }

    [Fact]
    public void Constructor_OneCharacterTooLong_IsRejected()
    {
        var tooLong = new string('x', Client.MaxNameLength + 1);

        Assert.ThrowsAny<ArgumentException>(() => new Client(tooLong));
    }
}
