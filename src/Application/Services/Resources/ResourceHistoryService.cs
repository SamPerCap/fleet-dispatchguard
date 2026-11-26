using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Resources;

/// <summary><c>Date</c> is the UTC instant in which the Madrid day starts.</summary>
public sealed record HistoryDayDto(DateTime Date, double Hours, int Trips, double Km);

/// <summary><c>TripsWithoutKm</c>: services whose km cannot be estimated.</summary>
public sealed record HistoryTotalsDto(double Hours, int Trips, double Km, int TripsWithoutKm, int DaysWorked, int DaysOverDaily, int DaysOverExtended);

/// <summary>A done service. <c>Km</c> is a straight-line estimate, or null.</summary>
public sealed record HistoryTripDto(
    int TripId, int BookingId, DateTime Start, DateTime End, double Hours, int OriginLocationId, int DestinationLocationId,
    string? Description, string ServiceType, int? DriverId, int? VehicleId, double? Km, LocalizedText? ForcedDescription);

/// <summary>The limits come from <c>DrivingLimits</c>, so that the web writes no figure.</summary>
public sealed record ResourceHistoryDto(
    string Resource, int ResourceId, DateTime At, DateTime From, DateTime To, int DaysCount,
    double DailyLimitHours, double ExtendedDailyLimitHours, int MaxExtendedDaysPerWeek,
    IReadOnlyList<HistoryDayDto> Days, HistoryTotalsDto Totals,
    IReadOnlyList<HistoryTripDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public interface IResourceHistoryService
{
    Task<ResourceHistoryDto> DriverAsync(int driverId, DateTime asOf, int page, int pageSize, CancellationToken ct = default);
    Task<ResourceHistoryDto> VehicleAsync(int vehicleId, DateTime asOf, int page, int pageSize, CancellationToken ct = default);
}

public sealed class ResourceHistoryService : IResourceHistoryService
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly ITripRepository _trips;
    private readonly IDriverRepository _drivers;
    private readonly IVehicleRepository _vehicles;
    private readonly ILocationRepository _locations;

    public ResourceHistoryService(ITripRepository trips, IDriverRepository drivers, IVehicleRepository vehicles, ILocationRepository locations)
    {
        _trips = trips ?? throw new ArgumentNullException(nameof(trips));
        _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
        _vehicles = vehicles ?? throw new ArgumentNullException(nameof(vehicles));
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
    }

    public async Task<ResourceHistoryDto> DriverAsync(int driverId, DateTime asOf, int page, int pageSize, CancellationToken ct = default)
    {
        Validate(page, pageSize);
        _ = await _drivers.GetByIdAsync(driverId, ct) ?? throw MissingResource.Driver(driverId);
        var from = ResourceHistory.Window(asOf)[0].From;
        var trips = await _trips.GetByDriverAsync(driverId, from, asOf, 1, int.MaxValue, cancellationToken: ct);
        return await BuildAsync("Driver", driverId, asOf, trips.Items, page, pageSize, ct);
    }

    public async Task<ResourceHistoryDto> VehicleAsync(int vehicleId, DateTime asOf, int page, int pageSize, CancellationToken ct = default)
    {
        Validate(page, pageSize);
        _ = await _vehicles.GetByIdAsync(vehicleId, ct) ?? throw MissingResource.Vehicle(vehicleId);
        var from = ResourceHistory.Window(asOf)[0].From;
        var trips = await _trips.GetByVehicleAsync(vehicleId, from, asOf, 1, int.MaxValue, cancellationToken: ct);
        return await BuildAsync("Vehicle", vehicleId, asOf, trips.Items, page, pageSize, ct);
    }

    private static void Validate(int page, int pageSize)
    {
        if (page < 1) throw new InvalidRequestException(Texts.FieldAtLeast(nameof(page), 1));
        if (pageSize < 1 || pageSize > MaxPageSize) throw new InvalidRequestException(Texts.FieldBetween(nameof(pageSize), 1, MaxPageSize));
    }

    private async Task<ResourceHistoryDto> BuildAsync(
        string resource, int id, DateTime asOf, IReadOnlyList<Trip> loaded, int page, int pageSize, CancellationToken ct)
    {
        var candidates = loaded.Where(t => ResourceHistory.IsDone(t.Status, t.ScheduledWindow.End, asOf)).ToList();
        var coordinates = await CoordinatesAsync(candidates, ct);
        var byId = candidates.ToDictionary(t => t.Id);
        var history = candidates
            .Select(t => new HistoryTrip(t.Id, t.ScheduledWindow.Start, t.ScheduledWindow.End, t.Status, Km(t, coordinates)))
            .Where(h => ResourceHistory.InWindow(h, asOf))
            .ToList();

        var days = ResourceHistory.Daily(history, asOf);
        var totals = ResourceHistory.Totals(days, history);
        var ordered = history.OrderByDescending(h => h.Start).ThenByDescending(h => h.TripId).ToList();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(h =>
        {
            var trip = byId[h.TripId];
            return new HistoryTripDto(trip.Id, trip.BookingId, h.Start, h.End, Hours(h.End - h.Start), trip.OriginLocationId, trip.DestinationLocationId,
                trip.Description, trip.ServiceType.ToString(), trip.AssignedDriverId, trip.AssignedVehicleId, h.Km, trip.ForcedDescription);
        }).ToList();

        return new ResourceHistoryDto(resource, id, asOf, days[0].From, asOf, ResourceHistory.Days,
            ResourceHistory.DailyLimit.TotalHours, ResourceHistory.ExtendedDailyLimit.TotalHours, DrivingLimits.MaxExtendedDaysPerWeek,
            days.Select(d => new HistoryDayDto(d.From, Hours(d.Duration), d.Trips, d.Km)).ToList(),
            new HistoryTotalsDto(Hours(totals.Duration), totals.Trips, totals.Km, totals.TripsWithoutKm, totals.DaysWorked,
                totals.DaysOverDaily, totals.DaysOverExtended),
            items, page, pageSize, ordered.Count, (int)Math.Ceiling(ordered.Count / (double)pageSize));
    }

    /// <summary>A location that cannot be read makes the trip not estimable.</summary>
    private static double? Km(Trip trip, IReadOnlyDictionary<int, GeoCoordinate> coordinates)
    {
        var ids = new[] { trip.OriginLocationId }
            .Concat(trip.IntermediateStops.OrderBy(s => s.SequenceOrder).Select(s => s.LocationId))
            .Append(trip.DestinationLocationId)
            .ToList();
        return ids.All(coordinates.ContainsKey) ? ResourceHistory.EstimateKm(ids.Select(i => coordinates[i]).ToList()) : null;
    }

    private async Task<IReadOnlyDictionary<int, GeoCoordinate>> CoordinatesAsync(IReadOnlyList<Trip> trips, CancellationToken ct)
    {
        var needed = trips.SelectMany(t => t.IntermediateStops.Select(s => s.LocationId).Append(t.OriginLocationId).Append(t.DestinationLocationId))
            .ToHashSet();
        var result = new Dictionary<int, GeoCoordinate>();
        if (needed.Count == 0) return result;
        foreach (var location in await _locations.GetAllAsync(ct))
            if (needed.Contains(location.Id)) result[location.Id] = location.Coordinate;
        // GetAllAsync skips inactive locations; old trips may still point at one.
        foreach (var missing in needed.Where(i => !result.ContainsKey(i)).ToList())
            if (await _locations.GetByIdAsync(missing, ct) is { } location) result[location.Id] = location.Coordinate;
        return result;
    }

    private static double Hours(TimeSpan time) => Math.Round(time.TotalHours, 2);
}
