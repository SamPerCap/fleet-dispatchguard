using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Resources;

/// <summary>SPEC 0090 R1: the version changes with the fields that can be edited, and with nothing else.</summary>
public class DetailsVersionTests
{
    private static readonly DateTime Day = new(2040, 5, 7, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, Action<Driver>> DriverChanges = new()
    {
        [nameof(UpdateDriverRequest.IdentificationNumber)] = d => d.ChangeDetails("87654321B", "Ana", "López", false, true),
        [nameof(UpdateDriverRequest.FirstName)] = d => d.ChangeDetails("12345678A", "Luisa", "López", false, true),
        [nameof(UpdateDriverRequest.LastName)] = d => d.ChangeDetails("12345678A", "Ana", "Gómez", false, true),
        [nameof(UpdateDriverRequest.IsExternal)] = d => d.ChangeDetails("12345678A", "Ana", "López", true, true),
        [nameof(UpdateDriverRequest.AllowsAutoAssignment)] = d => d.ChangeDetails("12345678A", "Ana", "López", false, false),
        [nameof(UpdateDriverRequest.HomeLocationId)] = d => d.SetHomeLocation(NewLocation()),
        [nameof(UpdateDriverRequest.BaseId)] = d => d.MoveToBase(2)
    };

    private static readonly Dictionary<string, Action<Vehicle>> VehicleChanges = new()
    {
        [nameof(UpdateVehicleRequest.LicensePlate)] = v => v.ChangeDetails("5678DEF", "Irizar", "i6", 55, false, true),
        [nameof(UpdateVehicleRequest.Brand)] = v => v.ChangeDetails("1234ABC", "Setra", "i6", 55, false, true),
        [nameof(UpdateVehicleRequest.Model)] = v => v.ChangeDetails("1234ABC", "Irizar", "i8", 55, false, true),
        [nameof(UpdateVehicleRequest.SeatingCapacity)] = v => v.ChangeDetails("1234ABC", "Irizar", "i6", 54, false, true),
        [nameof(UpdateVehicleRequest.IsExternal)] = v => v.ChangeDetails("1234ABC", "Irizar", "i6", 55, true, true),
        [nameof(UpdateVehicleRequest.AllowsAutoAssignment)] = v => v.ChangeDetails("1234ABC", "Irizar", "i6", 55, false, false),
        [nameof(UpdateVehicleRequest.HomeLocationId)] = v => v.SetHomeLocation(NewLocation()),
        [nameof(UpdateVehicleRequest.BaseId)] = v => v.MoveToBase(2)
    };

    private static Driver NewDriver() => new("12345678A", "Ana", "López");

    private static Vehicle NewVehicle() => new("1234ABC", "Irizar", "i6", 55);

    private static Location NewLocation() => new("Cochera", "Calle 1", new GeoCoordinate(40.4m, -3.7m), "Madrid", "Madrid", "ES");

    /// <summary>A field added to a request has to be added here, and the version must see it.</summary>
    [Fact]
    public void EveryFieldOfTheRequest_IsCovered()
    {
        Assert.Equal(EditableFields(typeof(UpdateDriverRequest)), DriverChanges.Keys.Order());
        Assert.Equal(EditableFields(typeof(UpdateVehicleRequest)), VehicleChanges.Keys.Order());
    }

    [Fact]
    public void Driver_EachEditableField_ChangesTheVersion()
    {
        var versions = DriverChanges.Values.Select(change => VersionAfter(NewDriver(), change)).Append(NewDriver().ToDto().DetailsVersion).ToList();

        Assert.Equal(DriverChanges.Count + 1, versions.Distinct().Count());
    }

    [Fact]
    public void Vehicle_EachEditableField_ChangesTheVersion()
    {
        var versions = VehicleChanges.Values.Select(change => VersionAfter(NewVehicle(), change)).Append(NewVehicle().ToDto().DetailsVersion).ToList();

        Assert.Equal(VehicleChanges.Count + 1, versions.Distinct().Count());
    }

    [Fact]
    public void Driver_WhatIsNotEdited_KeepsTheVersion()
    {
        var before = NewDriver().ToDto().DetailsVersion;

        Assert.Equal(before, VersionAfter(NewDriver(), d => d.AddUnavailability(
            new DriverUnavailability(1, new TimeWindow(Day, Day.AddDays(7)), UnavailabilityType.Vacation, "Verano"))));
        Assert.Equal(before, VersionAfter(NewDriver(), d => d.DeclareLastWeeklyRest(Day, Day.AddDays(1))));
        Assert.Equal(before, VersionAfter(NewDriver(), d => d.Deactivate()));
        Assert.Equal(before, VersionAfter(NewDriver(), d => d.ChangeDetails(" 12345678a ", " Ana ", "López", false, true)));
    }

    [Fact]
    public void Vehicle_WhatIsNotEdited_KeepsTheVersion()
    {
        var before = NewVehicle().ToDto().DetailsVersion;

        Assert.Equal(before, VersionAfter(NewVehicle(), v => v.AddUnavailability(
            new VehicleUnavailability(1, new TimeWindow(Day, Day.AddDays(1)), VehicleUnavailabilityType.Repair, "Frenos", "Taller"))));
        Assert.Equal(before, VersionAfter(NewVehicle(), v => v.Deactivate()));
    }

    [Fact]
    public void ChangedAndPutBack_IsTheSameVersion()
    {
        var driver = NewDriver();
        var before = driver.ToDto().DetailsVersion;

        driver.ChangeDetails("12345678A", "Luisa", "López", false, true);
        driver.ChangeDetails("12345678A", "Ana", "López", false, true);

        Assert.Equal(before, driver.ToDto().DetailsVersion);
    }

    /// <summary>The same letters, split between the two fields in another place.</summary>
    [Theory]
    [InlineData("Ana", "Belén López", "Ana Belén", "López")]
    [InlineData("A", "\",\"B", "A\",\"", "B")]
    [InlineData("A|", "B", "A", "|B")]
    public void Texts_AreNotMistakenForEachOther(string first, string last, string otherFirst, string otherLast) =>
        Assert.NotEqual(
            new Driver("12345678A", first, last).ToDto().DetailsVersion,
            new Driver("12345678A", otherFirst, otherLast).ToDto().DetailsVersion);

    [Fact]
    public void OfTheSameData_IsAcceptedAsAVersion()
    {
        var version = NewDriver().ToDto().DetailsVersion;

        DetailsVersion.EnsureWellFormed(version);
    }

    public static TheoryData<string?> NotVersions => new()
    {
        null, "", " ", "abc", new string('0', 63), new string('0', 65), new string('g', 64), new string('A', 64), new string('x', 5000)
    };

    [Theory]
    [MemberData(nameof(NotVersions))]
    public void AnythingElse_IsNotAVersion(string? text) =>
        Assert.ThrowsAny<ArgumentException>(() => DetailsVersion.EnsureWellFormed(text));

    private static string VersionAfter(Driver driver, Action<Driver> change)
    {
        change(driver);
        return driver.ToDto().DetailsVersion;
    }

    private static string VersionAfter(Vehicle vehicle, Action<Vehicle> change)
    {
        change(vehicle);
        return vehicle.ToDto().DetailsVersion;
    }

    private static IEnumerable<string> EditableFields(Type request) =>
        request.GetProperties().Select(p => p.Name).Where(name => name != nameof(UpdateDriverRequest.DetailsVersion)).Order();
}
