using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Entities;

/// <summary>Limits of the agenda entries (SPEC 0078 R1–R3).</summary>
public class UnavailabilityTests
{
    private static readonly DateTime Start = new(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddHours(2);
    private static readonly TimeWindow Period = new(Start, End);

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Notes_OfDriverAndVehicle_UpTo500Characters(int length, bool accepted)
    {
        var notes = new string('x', length);
        var writes = new Action[]
        {
            () => _ = new DriverUnavailability(1, Period, UnavailabilityType.Vacation, notes),
            () => _ = new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Cleaning, notes),
            () => new DriverUnavailability(1, Period, UnavailabilityType.Vacation).Change(Period, UnavailabilityType.Vacation, notes),
            () => new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Cleaning).Change(Period, VehicleUnavailabilityType.Cleaning, notes, null)
        };

        Assert.All(writes, write => AssertAccepted(accepted, write));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void ServiceProvider_UpTo200Characters(int length, bool accepted)
    {
        var workshop = new string('x', length);

        AssertAccepted(accepted, () => _ = new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Repair, null, workshop));
        AssertAccepted(accepted, () =>
            new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Repair).Change(Period, VehicleUnavailabilityType.Repair, null, workshop));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTexts_AreNoText(string? blank)
    {
        var entry = new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Repair, blank, blank);

        Assert.Null(entry.Notes);
        Assert.Null(entry.ServiceProvider);
        Assert.Null(new DriverUnavailability(1, Period, UnavailabilityType.Other, blank).Notes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Period_EmptyOrInverted_IsRejectedInSpanish(int minutes)
    {
        var rejected = Assert.ThrowsAny<ArgumentException>(() => AgendaEntryRules.Period(Start, Start.AddMinutes(minutes)));

        Assert.Equal("El fin debe ser posterior al inicio", Assert.IsType<InvalidRequestException>(rejected).Text.Es());
        Assert.Equal(Start.AddMinutes(1), AgendaEntryRules.Period(Start, Start.AddMinutes(1)).End);
    }

    [Fact]
    public void Period_WithoutStart_IsRejected() =>
        Assert.ThrowsAny<ArgumentException>(() => AgendaEntryRules.Period(default, End));

    [Theory]
    [InlineData(-180, false)] // not started
    [InlineData(-120, false)] // starts now
    [InlineData(-60, false)] // running
    [InlineData(-1, false)]
    [InlineData(0, true)] // [start, end): at its end it is already over
    [InlineData(1, true)]
    public void IsFinished_FromItsEndOn(int minutesFromEnd, bool finished)
    {
        var entry = new DriverUnavailability(1, Period, UnavailabilityType.Vacation);

        Assert.Equal(finished, AgendaEntryRules.IsFinished(entry, End.AddMinutes(minutesFromEnd)));
    }

    [Fact]
    public void Finished_SaysWhenInMadridTime() => Assert.Equal(
        "Esta entrada terminó el 24/11/2026 11:00: queda como historial y no se puede cambiar ni borrar",
        new UnavailabilityFinishedException(End).Text.Es());

    [Fact]
    public void ChangeUnavailability_ReplacesEverythingAndStampsTheVehicle()
    {
        var vehicle = new Vehicle("1234ABC", "Irizar", "i6", 55);
        var entry = new VehicleUnavailability(1, Period, VehicleUnavailabilityType.Repair, "Embrague", "Taller Norte");
        vehicle.AddUnavailability(entry);
        var later = new TimeWindow(End, End.AddDays(1));

        vehicle.ChangeUnavailability(entry, e => e.Change(later, VehicleUnavailabilityType.TechnicalInspection, " ", "ITV Getafe"));

        Assert.Equal((later, VehicleUnavailabilityType.TechnicalInspection, null, "ITV Getafe"),
            (entry.Period, entry.Type, entry.Notes, entry.ServiceProvider));
        Assert.NotNull(entry.Audit.UpdatedAt);
        Assert.NotNull(vehicle.Audit.UpdatedAt); // the exclusive operation recognises its commit by this stamp (SPEC 0065)
    }

    [Fact]
    public void ChangeUnavailability_OfAnotherResource_IsRejected()
    {
        var driver = new Driver("12345678Z", "Ana", "López");
        var foreign = new DriverUnavailability(2, Period, UnavailabilityType.Vacation);

        Assert.ThrowsAny<ArgumentException>(() => driver.ChangeUnavailability(foreign, _ => { }));
    }

    private static void AssertAccepted(bool accepted, Action action)
    {
        if (accepted)
        {
            action();
            return;
        }

        Assert.ThrowsAny<ArgumentException>(action);
    }
}
