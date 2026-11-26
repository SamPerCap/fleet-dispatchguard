using DispatchGuard.Core.Entities;

namespace Core.Tests.Entities;

public class DriverTests
{
    private static readonly DateTime Now = new(2035, 3, 6, 10, 0, 0, DateTimeKind.Utc);

    private static Driver NewDriver() => new("12345678A", "Ana", "López");

    [Fact]
    public void DeclareLastWeeklyRest_PastDate_IsStored()
    {
        var driver = NewDriver();

        driver.DeclareLastWeeklyRest(new DateTime(2035, 3, 4, 6, 0, 0, DateTimeKind.Utc), Now);

        Assert.Equal(new DateTime(2035, 3, 4, 6, 0, 0, DateTimeKind.Utc), driver.DeclaredLastWeeklyRestEnd);
    }

    [Fact]
    public void DeclareLastWeeklyRest_ExactlyNow_IsStored()
    {
        var driver = NewDriver();

        driver.DeclareLastWeeklyRest(Now, Now);

        Assert.Equal(Now, driver.DeclaredLastWeeklyRestEnd);
    }

    [Fact]
    public void DeclareLastWeeklyRest_OneMinuteInTheFuture_Throws()
    {
        var driver = NewDriver();

        Assert.ThrowsAny<ArgumentException>(() => driver.DeclareLastWeeklyRest(Now.AddMinutes(1), Now));
        Assert.Null(driver.DeclaredLastWeeklyRestEnd);
    }

    [Fact]
    public void NewDriver_HasNoDeclaredWeeklyRest()
    {
        Assert.Null(NewDriver().DeclaredLastWeeklyRestEnd);
    }
}
