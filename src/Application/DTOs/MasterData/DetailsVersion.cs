using System.Security.Cryptography;
using System.Text.Json;
using DispatchGuard.Core.Entities;

namespace DispatchGuard.Application.DTOs.MasterData;

/// <summary>
/// Fingerprint of the data that the card edits (SPEC 0090). A <c>rowversion</c> would also change with the agenda and turn adding
/// holidays into a conflict with whoever is fixing a plate.
/// </summary>
public static class DetailsVersion
{
    private const int HexLength = SHA256.HashSizeInBytes * 2;

    // Taken over the requests themselves: their fields are required, so a new one does not compile until it is also here.

    public static string Of(Driver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return Fingerprint(new UpdateDriverRequest
        {
            IdentificationNumber = driver.IdentificationNumber,
            FirstName = driver.FirstName,
            LastName = driver.LastName,
            IsExternal = driver.IsExternal,
            AllowsAutoAssignment = driver.AllowsAutoAssignment,
            HomeLocationId = driver.HomeLocationId,
            BaseId = driver.BaseId,
            DetailsVersion = string.Empty
        });
    }

    public static string Of(Vehicle vehicle)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        return Fingerprint(new UpdateVehicleRequest
        {
            LicensePlate = vehicle.LicensePlate,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            SeatingCapacity = vehicle.SeatingCapacity,
            IsExternal = vehicle.IsExternal,
            AllowsAutoAssignment = vehicle.AllowsAutoAssignment,
            HomeLocationId = vehicle.HomeLocationId,
            BaseId = vehicle.BaseId,
            DetailsVersion = string.Empty
        });
    }

    /// <summary>Anything else is a wrong request, not somebody else's change.</summary>
    public static void EnsureWellFormed(string? version)
    {
        if (version is not { Length: HexLength } || !version.All(char.IsAsciiHexDigitLower))
            throw new InvalidRequestException(Texts.DetailsVersionMalformed());
    }

    private static string Fingerprint<TDetails>(TDetails details) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(details)));
}
