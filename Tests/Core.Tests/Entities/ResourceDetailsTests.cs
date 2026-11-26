using DispatchGuard.Core.Entities;

namespace Core.Tests.Entities;

/// <summary>Limits of the data of a driver or vehicle, the same when creating and when changing (SPEC 0079 R1, R2).</summary>
public class ResourceDetailsTests
{
    private static Driver NewDriver() => new("12345678A", "Ana", "López");

    private static Vehicle NewVehicle() => new("1234ABC", "Irizar", "i6", 55);

    [Fact]
    public void ChangeDriver_AllFields_AreNormalised()
    {
        var driver = NewDriver();

        driver.ChangeDetails(" x9876543b ", " Luisa ", " Gómez Ruiz ", isExternal: true, allowsAutoAssignment: false);

        Assert.Equal(("X9876543B", "Luisa", "Gómez Ruiz"), (driver.IdentificationNumber, driver.FirstName, driver.LastName));
        Assert.Equal((true, false), (driver.IsExternal, driver.AllowsAutoAssignment));
        Assert.NotNull(driver.Audit.UpdatedAt);
    }

    [Fact]
    public void ChangeVehicle_AllFields_AreNormalised()
    {
        var vehicle = NewVehicle();

        vehicle.ChangeDetails(" 5678def ", " Setra ", " S 515 ", 49, isExternal: true, allowsAutoAssignment: false);

        Assert.Equal(("5678DEF", "Setra", "S 515", 49), (vehicle.LicensePlate, vehicle.Brand, vehicle.Model, vehicle.SeatingCapacity));
        Assert.Equal((true, false), (vehicle.IsExternal, vehicle.AllowsAutoAssignment));
        Assert.NotNull(vehicle.Audit.UpdatedAt);
    }

    public static TheoryData<int, int, int, string?> DriverLimits => new()
    {
        { 1, 1, 1, null },
        { 50, 100, 100, null },
        { 51, 100, 100, "El DNI no puede pasar de 50 caracteres" },
        { 50, 101, 100, "El nombre no puede pasar de 100 caracteres" },
        { 50, 100, 101, "Los apellidos no pueden pasar de 100 caracteres" },
        { 0, 1, 1, "Falta el DNI" },
        { 1, 0, 1, "Falta el nombre" },
        { 1, 1, 0, "Faltan los apellidos" }
    };

    [Theory]
    [MemberData(nameof(DriverLimits))]
    public void Driver_TextLimits_WhenCreatingAndWhenChanging(int number, int firstName, int lastName, string? error)
    {
        var driver = NewDriver();

        var creating = Record.Exception(() => new Driver(Text(number), Text(firstName), Text(lastName)));
        var changing = Record.Exception(() => driver.ChangeDetails(Text(number), Text(firstName), Text(lastName), true, false));

        Assert.Equal(error, MessageOf(creating));
        Assert.Equal(error, MessageOf(changing));
        if (error is not null) // All or nothing.
        {
            Assert.Equal(("12345678A", "Ana", "López", false, true),
                (driver.IdentificationNumber, driver.FirstName, driver.LastName, driver.IsExternal, driver.AllowsAutoAssignment));
            Assert.Null(driver.Audit.UpdatedAt);
        }
    }

    public static TheoryData<int, int, int, int, string?> VehicleLimits => new()
    {
        { 1, 1, 1, 1, null },
        { 20, 100, 100, int.MaxValue, null },
        { 21, 100, 100, 55, "La matrícula no puede pasar de 20 caracteres" },
        { 20, 101, 100, 55, "La marca no puede pasar de 100 caracteres" },
        { 20, 100, 101, 55, "El modelo no puede pasar de 100 caracteres" },
        { 0, 1, 1, 55, "Falta la matrícula" },
        { 1, 0, 1, 55, "Falta la marca" },
        { 1, 1, 0, 55, "Falta el modelo" },
        { 1, 1, 1, 0, "Las plazas deben ser 1 o más" },
        { 1, 1, 1, -5, "Las plazas deben ser 1 o más" }
    };

    [Theory]
    [MemberData(nameof(VehicleLimits))]
    public void Vehicle_Limits_WhenCreatingAndWhenChanging(int plate, int brand, int model, int seats, string? error)
    {
        var vehicle = NewVehicle();

        var creating = Record.Exception(() => new Vehicle(Text(plate), Text(brand), Text(model), seats));
        var changing = Record.Exception(() => vehicle.ChangeDetails(Text(plate), Text(brand), Text(model), seats, true, false));

        Assert.Equal(error, MessageOf(creating));
        Assert.Equal(error, MessageOf(changing));
        if (error is not null) // All or nothing.
        {
            Assert.Equal(("1234ABC", "Irizar", "i6", 55, false, true),
                (vehicle.LicensePlate, vehicle.Brand, vehicle.Model, vehicle.SeatingCapacity, vehicle.IsExternal, vehicle.AllowsAutoAssignment));
            Assert.Null(vehicle.Audit.UpdatedAt);
        }
    }

    [Fact]
    public void LongestText_WithSpacesAround_IsAccepted()
    {
        var driver = NewDriver();

        driver.ChangeDetails("1", $"  {Text(100)}  ", "B", false, true);

        Assert.Equal(100, driver.FirstName.Length);
    }

    [Fact]
    public void NullTexts_AreMissing()
    {
        Assert.Equal("Falta el nombre", MessageOf(Record.Exception(() => NewDriver().ChangeDetails("1", null!, "B", false, true))));
        Assert.Equal("Falta la marca", MessageOf(Record.Exception(() => NewVehicle().ChangeDetails("1", null!, "M", 5, false, true))));
    }

    /// <summary>A service of 50 seats (R5): exactly the seats it needs is enough.</summary>
    [Theory]
    [InlineData(50, true)]
    [InlineData(49, false)]
    public void Vehicle_LoweredSeats_DecideTheCapacity(int seats, bool enough)
    {
        var vehicle = NewVehicle();

        vehicle.ChangeDetails(vehicle.LicensePlate, vehicle.Brand, vehicle.Model, seats, false, true);

        Assert.Equal(enough, vehicle.HasCapacityFor(50));
    }

    [Fact]
    public void ChangeDetails_KeepsWhatIsNotPartOfIt()
    {
        var vehicle = NewVehicle();
        vehicle.MoveToBase(3);
        vehicle.UpdateOdometer(120_000);

        vehicle.ChangeDetails("5678DEF", "Setra", "S 515", 49, false, true);

        Assert.Equal((3, 120_000), (vehicle.BaseId, vehicle.OdometerKm));
    }

    private static string Text(int length) => new('X', length);

    /// <summary>The whole message is compared: a parameter name would add an English suffix that the dispatcher reads.</summary>
    private static string? MessageOf(Exception? exception) =>
        exception is null ? null : Assert.IsType<InvalidRequestException>(exception).Text.Es();
}
