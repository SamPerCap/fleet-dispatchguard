using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Application.DTOs.MasterData;

// Domain entities never leave the Application layer: the endpoints only see these records.

/// <summary><c>ServiceProvider</c>: workshop, only of vehicles.</summary>
public sealed record UnavailabilityDto(int Id, DateTime Start, DateTime End, string Type, string? Notes, string? ServiceProvider = null);

public sealed record CreateDriverUnavailabilityRequest(DateTime Start, DateTime End, UnavailabilityType Type, string? Notes);

public sealed record CreateVehicleUnavailabilityRequest(DateTime Start, DateTime End, VehicleUnavailabilityType Type, string? Notes, string? ServiceProvider);

public sealed record UpdateDriverUnavailabilityRequest(DateTime Start, DateTime End, UnavailabilityType Type, string? Notes);

public sealed record UpdateVehicleUnavailabilityRequest(DateTime Start, DateTime End, VehicleUnavailabilityType Type, string? Notes, string? ServiceProvider);

public sealed record DriverDto(
    int Id,
    string IdentificationNumber,
    string FirstName,
    string LastName,
    bool IsExternal,
    bool AllowsAutoAssignment,
    bool IsActive,
    int? HomeLocationId,
    DateTime? DeclaredLastWeeklyRestEnd,
    IReadOnlyList<UnavailabilityDto> Unavailabilities,
    string DetailsVersion,
    int BaseId = 0);

public sealed record CreateDriverRequest(
    string IdentificationNumber,
    string FirstName,
    string LastName,
    bool IsExternal = false,
    bool AllowsAutoAssignment = true,
    int? HomeLocationId = null,
    DateTime? DeclaredLastWeeklyRestEnd = null,
    int? BaseId = null);

/// <summary>Every field is required: one left out must not become "no location" or "not external" (SPEC 0079 R2).</summary>
public sealed record UpdateDriverRequest
{
    public required string IdentificationNumber { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required bool IsExternal { get; init; }
    public required bool AllowsAutoAssignment { get; init; }
    public required int? HomeLocationId { get; init; }
    public required int BaseId { get; init; }

    /// <summary>The one that came with the data the change starts from (SPEC 0090).</summary>
    public required string DetailsVersion { get; init; }
}

public sealed record DeclareWeeklyRestRequest(DateTime RestEnd);

public sealed record VehicleDto(
    int Id,
    string LicensePlate,
    string Brand,
    string Model,
    int SeatingCapacity,
    bool IsExternal,
    bool AllowsAutoAssignment,
    bool IsActive,
    int? HomeLocationId,
    IReadOnlyList<UnavailabilityDto> Unavailabilities,
    string DetailsVersion,
    int BaseId = 0);

public sealed record CreateVehicleRequest(
    string LicensePlate,
    string Brand,
    string Model,
    int SeatingCapacity,
    bool IsExternal = false,
    bool AllowsAutoAssignment = true,
    int? HomeLocationId = null,
    int? BaseId = null);

/// <inheritdoc cref="UpdateDriverRequest"/>
public sealed record UpdateVehicleRequest
{
    public required string LicensePlate { get; init; }
    public required string Brand { get; init; }
    public required string Model { get; init; }
    public required int SeatingCapacity { get; init; }
    public required bool IsExternal { get; init; }
    public required bool AllowsAutoAssignment { get; init; }
    public required int? HomeLocationId { get; init; }
    public required int BaseId { get; init; }

    /// <summary>The one that came with the data the change starts from (SPEC 0090).</summary>
    public required string DetailsVersion { get; init; }
}

public sealed record LocationDto(
    int Id, string Name, string Address, decimal Latitude, decimal Longitude,
    string City, string Province, string CountryCode, bool IsActive);

public sealed record CreateLocationRequest(
    string Name, string Address, decimal Latitude, decimal Longitude,
    string City, string Province, string CountryCode = "ES");

public sealed record TripDto(
    int Id,
    int OriginLocationId,
    int DestinationLocationId,
    DateTime Start,
    DateTime End,
    int RequiredSeats,
    string ServiceType,
    string Status,
    string? Description,
    int? AssignedDriverId,
    int? AssignedVehicleId,
    bool IsLocked,
    bool IsActive,
    int BookingId,
    int BaseId,
    string? ForcedReason = null,
    string? ForcedBy = null,
    LocalizedText? ForcedDescription = null)
{
    /// <summary>Assigned past a CE 561/2006 rejection, or illegal because of a service that was.</summary>
    public bool IsForced => ForcedDescription is not null;
}

public sealed record CreateTripRequest(
    int OriginLocationId,
    int DestinationLocationId,
    DateTime Start,
    DateTime End,
    int RequiredSeats,
    ServiceType ServiceType,
    string? Description = null,
    int? BaseId = null);

public sealed record BaseRequest(string Name);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>New resources are projected by hand, without AutoMapper (SPEC 0036).</summary>
public static class MasterDataMappings
{
    public static DriverDto ToDto(this Driver driver) => new(
        driver.Id, driver.IdentificationNumber, driver.FirstName, driver.LastName, driver.IsExternal, driver.AllowsAutoAssignment,
        driver.IsActive, driver.HomeLocationId, driver.DeclaredLastWeeklyRestEnd,
        driver.Unavailabilities.OrderBy(u => u.Period.Start)
            .Select(u => new UnavailabilityDto(u.Id, u.Period.Start, u.Period.End, u.Type.ToString(), u.Notes)).ToList(),
        DetailsVersion.Of(driver), driver.BaseId);

    public static VehicleDto ToDto(this Vehicle vehicle) => new(
        vehicle.Id, vehicle.LicensePlate, vehicle.Brand, vehicle.Model, vehicle.SeatingCapacity, vehicle.IsExternal, vehicle.AllowsAutoAssignment,
        vehicle.IsActive, vehicle.HomeLocationId,
        vehicle.Unavailabilities.OrderBy(u => u.Period.Start)
            .Select(u => new UnavailabilityDto(u.Id, u.Period.Start, u.Period.End, u.Type.ToString(), u.Notes, u.ServiceProvider)).ToList(),
        DetailsVersion.Of(vehicle), vehicle.BaseId);

    public static LocationDto ToDto(this Location location) => new(
        location.Id, location.Name, location.Address, location.Coordinate.Latitude, location.Coordinate.Longitude,
        location.City, location.Province, location.CountryCode, location.IsActive);

    public static TripDto ToDto(this Trip trip) => new(
        trip.Id, trip.OriginLocationId, trip.DestinationLocationId, trip.ScheduledWindow.Start, trip.ScheduledWindow.End,
        trip.RequiredSeats, trip.ServiceType.ToString(), trip.Status.ToString(), trip.Description,
        trip.AssignedDriverId, trip.AssignedVehicleId, trip.IsLocked, trip.IsActive, trip.BookingId, trip.BaseId, trip.ForcedReason, trip.ForcedBy, trip.ForcedDescription);
}
