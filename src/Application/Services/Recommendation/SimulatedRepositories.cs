using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Recommendation;

/// <summary>
/// Same interfaces and same answers as the database repositories, so that the engine's own classes run on the copy unchanged
/// (SPEC 0062, ADR 0020). <see cref="Assign"/> is the only change it accepts and it lives only here; every write throws.
/// </summary>
internal sealed class SimulatedTrips : ITripRepository, IDrivingRecordRepository
{
    private readonly List<Trip> _all;
    private readonly Dictionary<int, Trip> _byId;
    private readonly Dictionary<int, List<Trip>> _byDriver = [];
    private readonly Dictionary<int, List<Trip>> _byVehicle = [];
    private readonly ILookup<int, Trip> _byBooking;

    public SimulatedTrips(IEnumerable<Trip> trips)
    {
        _all = trips.OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToList();
        _byId = _all.ToDictionary(t => t.Id);
        _byBooking = _all.ToLookup(t => t.BookingId);
        foreach (var trip in _all)
        {
            if (trip.AssignedDriverId is int driver) Add(_byDriver, driver, trip);
            if (trip.AssignedVehicleId is int vehicle) Add(_byVehicle, vehicle, trip);
        }
    }

    /// <summary>The domain's own assignment, on the copy.</summary>
    public void Assign(Trip trip, Driver driver, Vehicle vehicle)
    {
        if (trip.AssignedDriverId is not null || trip.AssignedVehicleId is not null)
        {
            throw new InvalidOperationException($"Trip {trip.Id} already has resources: a recommendation never replaces them");
        }

        trip.AssignResources(driver, vehicle);
        Add(_byDriver, driver.Id, trip);
        Add(_byVehicle, vehicle.Id, trip);
    }

    public IEnumerable<Trip> OfDriver(int driverId) => Of(_byDriver, driverId);

    public IEnumerable<Trip> OfVehicle(int vehicleId) => Of(_byVehicle, vehicleId);

    public Task<Trip?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<IEnumerable<Trip>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(_all.Where(t => t.IsActive));

    public Task<PagedResult<Trip>> GetByDriverAsync(int driverId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Page(Overlapping(OfDriver(driverId), from, to).Where(t => t.Id != excludeTripId), page, pageSize));

    public Task<PagedResult<Trip>> GetByVehicleAsync(int vehicleId, DateTime? from, DateTime? to, int page, int pageSize, int? excludeTripId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Page(Overlapping(OfVehicle(vehicleId), from, to).Where(t => t.Id != excludeTripId), page, pageSize));

    public Task<PagedResult<Trip>> GetByDateRangeAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(Page(Overlapping(_all, from, to), page, pageSize));

    public Task<IReadOnlyList<Trip>> GetByBookingAsync(int bookingId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Trip>>(_byBooking[bookingId].ToList());

    public Task<IReadOnlyList<DrivingPeriod>> GetRecordsAsync(int driverId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("endDate must not be before startDate", nameof(endDate));
        }

        return Task.FromResult<IReadOnlyList<DrivingPeriod>>(Overlapping(OfDriver(driverId), startDate, endDate)
            .Where(t => TripStatusRules.Driving.Contains(t.Status))
            .Select(t => new DrivingPeriod(driverId, t.ScheduledWindow.Start, t.ScheduledWindow.End, t.Id))
            .ToList());
    }

    // Nothing that decides about an assignment asks for these.
    public Task<IReadOnlyList<TimeSpan>> GetDurationsAsync(int originLocationId, int destinationLocationId, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task<IReadOnlyList<Trip>> GetForcedByTripsAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task<PagedResult<Trip>> GetByStatusAsync(TripStatus status, int page, int pageSize, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task<Trip> AddAsync(Trip entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task UpdateAsync(Trip entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task DeleteAsync(Trip entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();

    private static IEnumerable<Trip> Of(Dictionary<int, List<Trip>> index, int id) => index.TryGetValue(id, out var trips) ? trips : [];

    private static void Add(Dictionary<int, List<Trip>> index, int id, Trip trip)
    {
        if (!index.TryGetValue(id, out var trips)) index[id] = trips = [];
        var at = trips.FindIndex(t => (t.ScheduledWindow.Start, t.Id).CompareTo((trip.ScheduledWindow.Start, trip.Id)) > 0);
        trips.Insert(at < 0 ? trips.Count : at, trip);
    }

    private static IEnumerable<Trip> Overlapping(IEnumerable<Trip> trips, DateTime? from, DateTime? to) =>
        trips.Where(t => (to is null || t.ScheduledWindow.Start < to) && (from is null || t.ScheduledWindow.End > from));

    private static PagedResult<Trip> Page(IEnumerable<Trip> trips, int page, int pageSize)
    {
        var items = trips.ToList();
        return PagedResult<Trip>.Create(items.Skip((int)Math.Min((long)(page - 1) * pageSize, items.Count)).Take(pageSize).ToList(), items.Count, page, pageSize);
    }
}

/// <summary><c>GetAllAsync</c> skips inactive rows and <c>GetByIdAsync</c> does not, as in the database.</summary>
internal abstract class Simulated<T> : IGenericRepository<T> where T : Entity
{
    private readonly Dictionary<int, T> _byId;

    protected Simulated(IEnumerable<T> entities) => _byId = entities.ToDictionary(e => e.Id);

    public T? Find(int id) => _byId.GetValueOrDefault(id);

    public Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Find(id));
    public Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(_byId.Values.Where(e => e.IsActive));
    public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
    public Task DeleteAsync(T entity, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
}

internal static class Simulated
{
    public static NotSupportedException Unsupported() => new("A simulation never writes (SPEC 0062)");
}

internal sealed class SimulatedDrivers(IEnumerable<Driver> drivers) : Simulated<Driver>(drivers), IDriverRepository
{
    public Task<bool> ExistsByIdentificationNumberAsync(string identificationNumber, int? exceptId = null, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
}

internal sealed class SimulatedVehicles(IEnumerable<Vehicle> vehicles) : Simulated<Vehicle>(vehicles), IVehicleRepository
{
    public Task<bool> ExistsByLicensePlateAsync(string licensePlate, int? exceptId = null, CancellationToken cancellationToken = default) => throw Simulated.Unsupported();
}

internal sealed class SimulatedLocations(IEnumerable<Location> locations) : Simulated<Location>(locations), ILocationRepository;
