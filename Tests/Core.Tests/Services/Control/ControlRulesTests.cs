using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Control;

public class ControlRulesTests
{
    private static readonly DateTime Now = new(2035, 1, 8, 11, 40, 0, DateTimeKind.Utc);

    [Fact]
    public void Light_NoIncidents_Green() => Assert.Equal(TrafficLight.Green, ControlRules.Light(0, 0));

    [Fact]
    public void Light_OnlyWarnings_Amber() => Assert.Equal(TrafficLight.Amber, ControlRules.Light(0, 3));

    [Fact]
    public void Light_AnyUrgent_Red() => Assert.Equal(TrafficLight.Red, ControlRules.Light(1, 0));

    [Fact]
    public void Light_UrgentAndWarnings_Red() => Assert.Equal(TrafficLight.Red, ControlRules.Light(1, 4));

    [Fact]
    public void IsUrgent_Boundary_OneHourIsNotUrgent()
    {
        Assert.True(ControlRules.IsUrgent(Now.AddMinutes(59), Now));
        Assert.False(ControlRules.IsUrgent(Now.AddHours(1), Now));
    }

    [Fact]
    public void Texts_SpecExample()
    {
        var (headline, detail) = ControlRules.Wording(urgent: 2, warnings: 3, covered: 10, total: 12);
        Assert.Equal("Quedan 5 tareas por resolver", headline.Es());
        Assert.Equal("2 urgentes y 3 avisos. 10 de 12 servicios cubiertos.", detail.Es());
    }

    [Fact]
    public void Texts_GreenAllCovered()
    {
        var (headline, detail) = ControlRules.Wording(urgent: 0, warnings: 0, covered: 12, total: 12);
        Assert.Equal("Todo cubierto", headline.Es());
        Assert.Equal("12 de 12 servicios cubiertos.", detail.Es());
    }

    [Fact]
    public void Texts_NoServices_Green()
    {
        var (headline, detail) = ControlRules.Wording(0, 0, 0, 0);
        Assert.Equal("Todo cubierto", headline.Es());
        Assert.Equal("0 de 0 servicios cubiertos.", detail.Es());
    }

    [Fact]
    public void Texts_NothingPendingButUncoveredTripsAlreadyEnded_NotAllCovered()
    {
        var (headline, detail) = ControlRules.Wording(0, 0, covered: 10, total: 12);
        Assert.Equal("Nada pendiente", headline.Es());
        Assert.Equal("10 de 12 servicios cubiertos.", detail.Es());
    }

    [Fact]
    public void Texts_Singular()
    {
        var (headline, detail) = ControlRules.Wording(urgent: 1, warnings: 0, covered: 3, total: 4);
        Assert.Equal("Queda 1 tarea por resolver", headline.Es());
        Assert.Equal("1 urgente y 0 avisos. 3 de 4 servicios cubiertos.", detail.Es());
    }

    [Fact]
    public void LimitsNear_Daily8h06_IsNinetyPercent()
    {
        var hours = DriverHours.Of(TimeSpan.FromMinutes(8 * 60 + 6), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);
        Assert.Equal([DrivingLimitKind.Daily], ControlRules.LimitsNear(hours));
    }

    [Fact]
    public void LimitsNear_Daily8h05_Nothing()
    {
        var hours = DriverHours.Of(TimeSpan.FromMinutes(8 * 60 + 5), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);
        Assert.Empty(ControlRules.LimitsNear(hours));
    }

    [Fact]
    public void LimitsNear_WeeklyAndBiWeekly_FromDrivingLimits()
    {
        var weekly = TimeSpan.FromTicks(DrivingLimits.MaxWeeklyDriving.Ticks * 9 / 10);
        var biWeekly = TimeSpan.FromTicks(DrivingLimits.MaxBiWeeklyDriving.Ticks * 9 / 10);
        var hours = DriverHours.Of(TimeSpan.Zero, weekly, biWeekly, TimeSpan.Zero, null);
        Assert.Equal([DrivingLimitKind.Weekly, DrivingLimitKind.BiWeekly], ControlRules.LimitsNear(hours));
        var below = DriverHours.Of(TimeSpan.Zero, weekly - TimeSpan.FromMinutes(1), biWeekly - TimeSpan.FromMinutes(1), TimeSpan.Zero, null);
        Assert.Empty(ControlRules.LimitsNear(below));
    }
}
