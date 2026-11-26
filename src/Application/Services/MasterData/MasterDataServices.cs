using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.MasterData;

// Invariant violations surface as ArgumentException (→ 400) and missing resources as KeyNotFoundException (→ 404) through the
// global handler; nothing is caught here.

public interface IDriverService
{
    Task<IReadOnlyList<DriverDto>> GetAllAsync(CancellationToken ct = default);
    Task<DriverDto> GetAsync(int id, CancellationToken ct = default);
    Task<DriverDto> CreateAsync(CreateDriverRequest request, CancellationToken ct = default);
    Task<DriverDto> UpdateAsync(int id, UpdateDriverRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<DriverDto> DeclareLastWeeklyRestAsync(int id, DateTime restEnd, CancellationToken ct = default);
    Task<DriverDto> AddUnavailabilityAsync(int id, CreateDriverUnavailabilityRequest request, CancellationToken ct = default);
    Task<DriverDto> ChangeUnavailabilityAsync(int id, int unavailabilityId, UpdateDriverUnavailabilityRequest request, CancellationToken ct = default);
    Task RemoveUnavailabilityAsync(int id, int unavailabilityId, CancellationToken ct = default);
}

public sealed class DriverService(IUnitOfWork uow, TimeProvider clock) : IDriverService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IReadOnlyList<DriverDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _uow.Drivers.GetAllAsync(ct)).Select(d => d.ToDto()).ToList();

    public async Task<DriverDto> GetAsync(int id, CancellationToken ct = default) => (await LoadAsync(id, ct)).ToDto();

    public async Task<DriverDto> CreateAsync(CreateDriverRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var driver = new Driver(request.IdentificationNumber, request.FirstName, request.LastName,
            request.IsExternal, request.AllowsAutoAssignment);
        if (await _uow.Drivers.ExistsByIdentificationNumberAsync(driver.IdentificationNumber, cancellationToken: ct))
            throw new InvalidRequestException(Texts.RepeatedIdentification(driver.IdentificationNumber));
        if (request.HomeLocationId is int locationId)
            driver.SetHomeLocation(await MasterDataLookups.ActiveLocationAsync(_uow, locationId, ct));
        if (request.BaseId is int driverBase)
            driver.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, driverBase, ct));
        if (request.DeclaredLastWeeklyRestEnd is DateTime restEnd)
            driver.DeclareLastWeeklyRest(restEnd, _clock.GetUtcNow().UtcDateTime);

        await _uow.Drivers.AddAsync(driver, ct);
        await _uow.SaveChangesAsync(ct);
        return driver.ToDto();
    }

    public async Task<DriverDto> UpdateAsync(int id, UpdateDriverRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DetailsVersion.EnsureWellFormed(request.DetailsVersion);
        return (await ChangeAsync(id, async driver =>
        {
            if (request.DetailsVersion != DetailsVersion.Of(driver)) throw new DetailsChangedException();
            driver.ChangeDetails(request.IdentificationNumber, request.FirstName, request.LastName, request.IsExternal, request.AllowsAutoAssignment);
            if (await _uow.Drivers.ExistsByIdentificationNumberAsync(driver.IdentificationNumber, driver.Id, ct))
                throw ValueAlreadyUsedException.IdentificationNumber(driver.IdentificationNumber);
            await MasterDataLookups.MoveAsync(_uow, driver, request.HomeLocationId, ct);
            if (request.BaseId != driver.BaseId)
                driver.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, request.BaseId, ct));
            await _uow.Drivers.UpdateAsync(driver, ct);
        }, ct)).ToDto();
    }

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        ChangeAsync(id, driver => _uow.Drivers.DeleteAsync(driver, ct), ct);

    public async Task<DriverDto> DeclareLastWeeklyRestAsync(int id, DateTime restEnd, CancellationToken ct = default) =>
        (await ChangeAsync(id, driver =>
        {
            driver.DeclareLastWeeklyRest(restEnd, _clock.GetUtcNow().UtcDateTime);
            return _uow.Drivers.UpdateAsync(driver, ct);
        }, ct)).ToDto();

    public async Task<DriverDto> AddUnavailabilityAsync(int id, CreateDriverUnavailabilityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MasterDataLookups.EnsureDefined(request.Type, nameof(request.Type));
        return (await ChangeAsync(id, driver =>
        {
            driver.AddUnavailability(new DriverUnavailability(driver.Id, AgendaEntryRules.Period(request.Start, request.End), request.Type, request.Notes));
            return _uow.Drivers.UpdateAsync(driver, ct);
        }, ct)).ToDto();
    }

    public async Task<DriverDto> ChangeUnavailabilityAsync(
        int id, int unavailabilityId, UpdateDriverUnavailabilityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MasterDataLookups.EnsureDefined(request.Type, nameof(request.Type));
        return (await ChangeAsync(id, driver =>
        {
            var period = AgendaEntryRules.Period(request.Start, request.End);
            driver.ChangeUnavailability(OpenEntry(driver, unavailabilityId), u => u.Change(period, request.Type, request.Notes));
            return _uow.Drivers.UpdateAsync(driver, ct);
        }, ct)).ToDto();
    }

    public Task RemoveUnavailabilityAsync(int id, int unavailabilityId, CancellationToken ct = default) =>
        ChangeAsync(id, driver =>
        {
            driver.RemoveUnavailability(OpenEntry(driver, unavailabilityId));
            return _uow.Drivers.UpdateAsync(driver, ct);
        }, ct);

    private DriverUnavailability OpenEntry(Driver driver, int unavailabilityId)
    {
        var entry = driver.Unavailabilities.FirstOrDefault(u => u.Id == unavailabilityId)
            ?? throw MissingResource.DriverUnavailability(unavailabilityId, driver.Id);
        return AgendaEntryRules.IsFinished(entry, _clock.GetUtcNow().UtcDateTime)
            ? throw new UnavailabilityFinishedException(entry.Period.End)
            : entry;
    }

    /// <summary>An allocation of the same driver sees the change whole or not at all (SPEC 0065).</summary>
    private Task<Driver> ChangeAsync(int id, Func<Driver, Task> change, CancellationToken ct) =>
        _uow.ExecuteExclusiveAsync(ResourceClaim.Driver(id), async token =>
        {
            var driver = await LoadAsync(id, token);
            await change(driver);
            await _uow.SaveChangesAsync(token);
            return driver;
        }, driver => driver, ct);

    private async Task<Driver> LoadAsync(int id, CancellationToken ct) =>
        await _uow.Drivers.GetByIdAsync(id, ct) ?? throw MissingResource.Driver(id);
}

public interface IVehicleService
{
    Task<IReadOnlyList<VehicleDto>> GetAllAsync(CancellationToken ct = default);
    Task<VehicleDto> GetAsync(int id, CancellationToken ct = default);
    Task<VehicleDto> CreateAsync(CreateVehicleRequest request, CancellationToken ct = default);
    Task<VehicleDto> UpdateAsync(int id, UpdateVehicleRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<VehicleDto> AddUnavailabilityAsync(int id, CreateVehicleUnavailabilityRequest request, CancellationToken ct = default);
    Task<VehicleDto> ChangeUnavailabilityAsync(int id, int unavailabilityId, UpdateVehicleUnavailabilityRequest request, CancellationToken ct = default);
    Task RemoveUnavailabilityAsync(int id, int unavailabilityId, CancellationToken ct = default);
}

public sealed class VehicleService(IUnitOfWork uow, TimeProvider clock) : IVehicleService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<IReadOnlyList<VehicleDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _uow.Vehicles.GetAllAsync(ct)).Select(v => v.ToDto()).ToList();

    public async Task<VehicleDto> GetAsync(int id, CancellationToken ct = default) => (await LoadAsync(id, ct)).ToDto();

    public async Task<VehicleDto> CreateAsync(CreateVehicleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var vehicle = new Vehicle(request.LicensePlate, request.Brand, request.Model, request.SeatingCapacity,
            request.IsExternal, request.AllowsAutoAssignment);
        if (await _uow.Vehicles.ExistsByLicensePlateAsync(vehicle.LicensePlate, cancellationToken: ct))
            throw new InvalidRequestException(Texts.RepeatedPlate(vehicle.LicensePlate));
        if (request.HomeLocationId is int locationId)
            vehicle.SetHomeLocation(await MasterDataLookups.ActiveLocationAsync(_uow, locationId, ct));
        if (request.BaseId is int vehicleBase)
            vehicle.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, vehicleBase, ct));

        await _uow.Vehicles.AddAsync(vehicle, ct);
        await _uow.SaveChangesAsync(ct);
        return vehicle.ToDto();
    }

    public async Task<VehicleDto> UpdateAsync(int id, UpdateVehicleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DetailsVersion.EnsureWellFormed(request.DetailsVersion);
        return (await ChangeAsync(id, async vehicle =>
        {
            if (request.DetailsVersion != DetailsVersion.Of(vehicle)) throw new DetailsChangedException();
            vehicle.ChangeDetails(request.LicensePlate, request.Brand, request.Model, request.SeatingCapacity, request.IsExternal, request.AllowsAutoAssignment);
            if (await _uow.Vehicles.ExistsByLicensePlateAsync(vehicle.LicensePlate, vehicle.Id, ct))
                throw ValueAlreadyUsedException.LicensePlate(vehicle.LicensePlate);
            await MasterDataLookups.MoveAsync(_uow, vehicle, request.HomeLocationId, ct);
            if (request.BaseId != vehicle.BaseId)
                vehicle.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, request.BaseId, ct));
            await _uow.Vehicles.UpdateAsync(vehicle, ct);
        }, ct)).ToDto();
    }

    public Task DeleteAsync(int id, CancellationToken ct = default) =>
        ChangeAsync(id, vehicle => _uow.Vehicles.DeleteAsync(vehicle, ct), ct);

    public async Task<VehicleDto> AddUnavailabilityAsync(int id, CreateVehicleUnavailabilityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MasterDataLookups.EnsureDefined(request.Type, nameof(request.Type));
        return (await ChangeAsync(id, vehicle =>
        {
            vehicle.AddUnavailability(new VehicleUnavailability(vehicle.Id, AgendaEntryRules.Period(request.Start, request.End), request.Type, request.Notes, request.ServiceProvider));
            return _uow.Vehicles.UpdateAsync(vehicle, ct);
        }, ct)).ToDto();
    }

    public async Task<VehicleDto> ChangeUnavailabilityAsync(
        int id, int unavailabilityId, UpdateVehicleUnavailabilityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MasterDataLookups.EnsureDefined(request.Type, nameof(request.Type));
        return (await ChangeAsync(id, vehicle =>
        {
            var period = AgendaEntryRules.Period(request.Start, request.End);
            vehicle.ChangeUnavailability(
                OpenEntry(vehicle, unavailabilityId), u => u.Change(period, request.Type, request.Notes, request.ServiceProvider));
            return _uow.Vehicles.UpdateAsync(vehicle, ct);
        }, ct)).ToDto();
    }

    public Task RemoveUnavailabilityAsync(int id, int unavailabilityId, CancellationToken ct = default) =>
        ChangeAsync(id, vehicle =>
        {
            vehicle.RemoveUnavailability(OpenEntry(vehicle, unavailabilityId));
            return _uow.Vehicles.UpdateAsync(vehicle, ct);
        }, ct);

    private VehicleUnavailability OpenEntry(Vehicle vehicle, int unavailabilityId)
    {
        var entry = vehicle.Unavailabilities.FirstOrDefault(u => u.Id == unavailabilityId)
            ?? throw MissingResource.VehicleUnavailability(unavailabilityId, vehicle.Id);
        return AgendaEntryRules.IsFinished(entry, _clock.GetUtcNow().UtcDateTime)
            ? throw new UnavailabilityFinishedException(entry.Period.End)
            : entry;
    }

    /// <summary>An allocation of the same vehicle sees the change whole or not at all (SPEC 0065).</summary>
    private Task<Vehicle> ChangeAsync(int id, Func<Vehicle, Task> change, CancellationToken ct) =>
        _uow.ExecuteExclusiveAsync(ResourceClaim.Vehicle(id), async token =>
        {
            var vehicle = await LoadAsync(id, token);
            await change(vehicle);
            await _uow.SaveChangesAsync(token);
            return vehicle;
        }, vehicle => vehicle, ct);

    private async Task<Vehicle> LoadAsync(int id, CancellationToken ct) =>
        await _uow.Vehicles.GetByIdAsync(id, ct) ?? throw MissingResource.Vehicle(id);
}

public interface ILocationService
{
    Task<IReadOnlyList<LocationDto>> GetAllAsync(CancellationToken ct = default);
    Task<LocationDto> GetAsync(int id, CancellationToken ct = default);
    Task<LocationDto> CreateAsync(CreateLocationRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class LocationService(IUnitOfWork uow) : ILocationService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));

    public async Task<IReadOnlyList<LocationDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _uow.Locations.GetAllAsync(ct)).Select(l => l.ToDto()).ToList();

    public async Task<LocationDto> GetAsync(int id, CancellationToken ct = default) => (await LoadAsync(id, ct)).ToDto();

    public async Task<LocationDto> CreateAsync(CreateLocationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var location = new Location(request.Name, request.Address, new GeoCoordinate(request.Latitude, request.Longitude),
            request.City, request.Province, request.CountryCode);
        await _uow.Locations.AddAsync(location, ct);
        await _uow.SaveChangesAsync(ct);
        return location.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await _uow.Locations.DeleteAsync(await LoadAsync(id, ct), ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task<Location> LoadAsync(int id, CancellationToken ct) =>
        await _uow.Locations.GetByIdAsync(id, ct) ?? throw MissingResource.Location(id);
}

public interface ITripService
{
    Task<PagedDto<TripDto>> GetByRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken ct = default);
    Task<TripDto> GetAsync(int id, CancellationToken ct = default);
    Task<TripDto> CreateAsync(CreateTripRequest request, CancellationToken ct = default);
    Task<TripDto> CancelAsync(int id, CancellationToken ct = default);
}

public sealed class TripService(IUnitOfWork uow, IForcedConsequences consequences) : ITripService
{
    public const int MaxPageSize = 200;

    /// <summary>Of one query; same as the optimizer.</summary>
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    private readonly IForcedConsequences _consequences = consequences ?? throw new ArgumentNullException(nameof(consequences));

    public async Task<PagedDto<TripDto>> GetByRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken ct = default)
    {
        if (to <= from) throw new InvalidRequestException(Texts.FieldAfter(nameof(to), nameof(from)));
        if (to - from > MaxRange) throw new InvalidRequestException(Texts.RangeTooLong(MaxRange.TotalDays));
        if (page < 1) throw new InvalidRequestException(Texts.FieldAtLeast(nameof(page), 1));
        if (pageSize < 1 || pageSize > MaxPageSize) throw new InvalidRequestException(Texts.FieldBetween(nameof(pageSize), 1, MaxPageSize));

        var result = await _uow.Trips.GetByDateRangeAsync(from, to, page, pageSize, ct);
        return new PagedDto<TripDto>(result.Items.Select(t => t.ToDto()).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    public async Task<TripDto> GetAsync(int id, CancellationToken ct = default) => (await LoadAsync(id, ct)).ToDto();

    public async Task<TripDto> CreateAsync(CreateTripRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        MasterDataLookups.EnsureDefined(request.ServiceType, nameof(request.ServiceType));
        var origin = await MasterDataLookups.ActiveLocationAsync(_uow, request.OriginLocationId, ct);
        var destination = await MasterDataLookups.ActiveLocationAsync(_uow, request.DestinationLocationId, ct);

        var trip = new Trip(origin, destination, new TimeWindow(request.Start, request.End),
            request.RequiredSeats, request.ServiceType, request.Description);
        if (request.BaseId is int tripBase)
            trip.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, tripBase, ct));
        await _uow.Trips.AddAsync(trip, ct);
        await _uow.SaveChangesAsync(ct);
        return trip.ToDto();
    }

    public async Task<TripDto> CancelAsync(int id, CancellationToken ct = default) =>
        (await _uow.ExecuteExclusiveAsync(ResourceClaim.Trips([id]), async token =>
        {
            var trip = await LoadAsync(id, token);
            trip.Cancel();
            await _uow.Trips.UpdateAsync(trip, token);
            var released = await _consequences.ReleaseAsync([trip.Id], token);
            await _uow.SaveChangesAsync(token);
            await _consequences.RevalidateAsync(released, token);
            return trip;
        }, trip => trip, ct)).ToDto();

    private async Task<Trip> LoadAsync(int id, CancellationToken ct) =>
        await _uow.Trips.GetByIdAsync(id, ct) ?? throw MissingResource.Trip(id);
}

internal static class MasterDataLookups
{
    public static async Task<Location> ActiveLocationAsync(IUnitOfWork uow, int id, CancellationToken ct)
    {
        var location = await uow.Locations.GetByIdAsync(id, ct);
        return location is { IsActive: true } ? location : throw MissingResource.Location(id);
    }

    /// <summary>A location deactivated after being chosen may stay: it must not block changing something else (SPEC 0079 R4).</summary>
    public static async Task MoveAsync(IUnitOfWork uow, EntityWithHomeLocation resource, int? homeLocationId, CancellationToken ct)
    {
        if (homeLocationId == resource.HomeLocationId) return;
        if (homeLocationId is int locationId) resource.SetHomeLocation(await ActiveLocationAsync(uow, locationId, ct));
        else resource.ClearHomeLocation();
    }

    /// <summary>An unknown or inactive base is a 404, never a foreign-key 500 (SPEC 0053).</summary>
    public static async Task<int> ActiveBaseIdAsync(IUnitOfWork uow, int id, CancellationToken ct)
    {
        var operatingBase = await uow.Bases.GetByIdAsync(id, ct);
        return operatingBase is { IsActive: true } ? operatingBase.Id : throw MissingResource.Base(id);
    }

    public static void EnsureDefined<TEnum>(TEnum value, string paramName) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new InvalidRequestException(Texts.UnknownValue(paramName, value.ToString()));
    }
}
