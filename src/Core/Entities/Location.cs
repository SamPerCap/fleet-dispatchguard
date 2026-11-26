using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class Location : Entity
{
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxCityOrProvinceLength = 100;
    public const int MaxCountryCodeLength = 2;

    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public GeoCoordinate Coordinate { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string Province { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;

    private Location() { }

    public Location(
        string name,
        string address,
        GeoCoordinate coordinate,
        string city,
        string province,
        string countryCode = "ES")
    {
        ValidateName(name);
        ValidateAddress(address);
        ValidateCoordinate(coordinate);
        ValidateCity(city);
        ValidateProvince(province);
        ValidateCountryCode(countryCode);

        Name = name.Trim();
        Address = address.Trim();
        Coordinate = coordinate;
        City = city.Trim();
        Province = province.Trim();
        CountryCode = countryCode;
    }

    public void UpdateCoordinates(GeoCoordinate newCoordinate)
    {
        ValidateCoordinate(newCoordinate);
        Coordinate = newCoordinate;
        MarkAsUpdated();
    }

    private static void ValidateName(string? name)
    {
        EntityValidator.ValidateRequiredString(name, nameof(name));
        EntityValidator.ValidateMaxLength(name!.Trim(), MaxNameLength, Texts.NameCannot());
    }

    private static void ValidateAddress(string? address)
    {
        EntityValidator.ValidateRequiredString(address, nameof(address));
        EntityValidator.ValidateMaxLength(address!.Trim(), MaxAddressLength, Texts.AddressCannot());
    }

    private static void ValidateCoordinate(GeoCoordinate? coordinate)
    {
        EntityValidator.ValidateNotNull(coordinate, nameof(coordinate));
    }

    private static void ValidateCity(string? city)
    {
        EntityValidator.ValidateRequiredString(city, nameof(city));
        EntityValidator.ValidateMaxLength(city!.Trim(), MaxCityOrProvinceLength, Texts.CityCannot());
    }

    private static void ValidateProvince(string? province)
    {
        EntityValidator.ValidateRequiredString(province, nameof(province));
        EntityValidator.ValidateMaxLength(province!.Trim(), MaxCityOrProvinceLength, Texts.ProvinceCannot());
    }

    private static void ValidateCountryCode(string? countryCode)
    {
        EntityValidator.ValidateRequiredString(countryCode, nameof(countryCode));
        EntityValidator.ValidateMaxLength(countryCode, MaxCountryCodeLength, Texts.CountryCodeCannot());
    }
}