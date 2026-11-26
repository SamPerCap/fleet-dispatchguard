using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Optimization;

/// <summary>
/// A proposal: nothing is persisted. <c>From</c>/<c>To</c> are UTC instants; for a week, its Madrid midnights (SPEC 0071).
/// <c>TripsDeparted</c>: how many of <c>TripsConsidered</c> have already departed and so keep their vehicle (SPEC 0070).
/// </summary>
public sealed record VehicleChainingDto(
    DateTime From,
    DateTime To,
    bool ReturnToBase,
    double AverageSpeedKmh,
    int TripsConsidered,
    int TripsDeparted,
    double BaselineDeadheadKm,
    double ProposedDeadheadKm,
    double SavingPercent,
    IReadOnlyList<VehicleChangeDto> Changes,
    DecisionExplanation Explanation);

public sealed record VehicleChangeDto(
    int TripId,
    DateTime Start,
    DateTime End,
    int DriverId,
    int CurrentVehicleId,
    string CurrentLicensePlate,
    int ProposedVehicleId,
    string ProposedLicensePlate,
    DecisionExplanation Explanation);

/// <summary>A synchronous Application service instead of a worker process: see ADR 0013.</summary>
public interface IVehicleChainingService
{
    /// <summary>For trips starting in <c>[from, to)</c>, UTC instants.</summary>
    /// <exception cref="ArgumentException">Invalid range.</exception>
    Task<VehicleChainingDto> Propose(DateTime from, DateTime to, bool returnToBase, CancellationToken cancellationToken = default);
}

public sealed class VehicleChainingService : IVehicleChainingService
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(31);

    /// <summary>31 Madrid days last one hour more in October (SPEC 0071 R4).</summary>
    private static readonly TimeSpan AutumnHour = TimeSpan.FromHours(1);

    private const int PageSize = 1000;

    private readonly ITripRepository _trips;
    private readonly IVehicleRepository _vehicles;
    private readonly ILocationRepository _locations;
    private readonly IVehicleEligibilityService _eligibility;
    private readonly IVehicleLastServiceReader _lastServices;
    private readonly TimeProvider _clock;

    public VehicleChainingService(ITripRepository trips, IVehicleRepository vehicles, ILocationRepository locations,
        IVehicleEligibilityService eligibility, IVehicleLastServiceReader lastServices, TimeProvider clock)
    {
        _lastServices = lastServices ?? throw new ArgumentNullException(nameof(lastServices));
        _trips = trips ?? throw new ArgumentNullException(nameof(trips));
        _vehicles = vehicles ?? throw new ArgumentNullException(nameof(vehicles));
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<VehicleChainingDto> Propose(DateTime from, DateTime to, bool returnToBase, CancellationToken cancellationToken = default)
    {
        if (from >= to)
        {
            throw new InvalidRequestException(Texts.FieldAfter("to", "from"));
        }

        if (to - from > MaxRange + AutumnHour)
        {
            throw new InvalidRequestException(Texts.RangeTooLong(MaxRange.TotalDays));
        }

        var trips = new List<Trip>();
        for (var page = 1; ; page++)
        {
            var result = await _trips.GetByDateRangeAsync(from, to, page, PageSize, cancellationToken);
            trips.AddRange(result.Items);
            if (result.Items.Count < PageSize)
            {
                break;
            }
        }

        // The repository returns overlapping trips, but only those that start in the range count (SPEC 0043 R5).
        trips = trips.Where(t => t.ScheduledWindow.Start >= from && t.ScheduledWindow.Start < to).ToList();
        var fleet = (await _vehicles.GetAllAsync(cancellationToken)).ToList();

        // GetAllAsync skips inactive vehicles; a trip may still hold one (it will fail eligibility and be replaced).
        foreach (var id in trips.Select(t => t.AssignedVehicleId).OfType<int>().Distinct().Except(fleet.Select(v => v.Id)).ToList())
        {
            if (await _vehicles.GetByIdAsync(id, cancellationToken) is Vehicle vehicle)
            {
                fleet.Add(vehicle);
            }
        }
        var locations = (await _locations.GetAllAsync(cancellationToken)).ToDictionary(l => l.Id);

        var now = _clock.GetUtcNow().UtcDateTime;
        var lastServices = await _lastServices.LastServicesAsync(fleet.Select(v => v.Id).ToList(), now, cancellationToken);
        var plan = VehicleChainingPlanner.Plan(trips, fleet, locations, returnToBase, _eligibility, now, lastServices);
        return new VehicleChainingDto(
            from, to, returnToBase, TravelAssumptions.AverageSpeedKmh, plan.TripsConsidered, plan.TripsDeparted,
            Math.Round(plan.BaselineDeadheadKm, 1), Math.Round(plan.ProposedDeadheadKm, 1), Math.Round(plan.SavingPercent, 1),
            plan.Changes.Select(c => new VehicleChangeDto(
                c.Trip.Id, c.Trip.ScheduledWindow.Start, c.Trip.ScheduledWindow.End, c.Trip.AssignedDriverId!.Value,
                c.Current.Id, c.Current.LicensePlate, c.Proposed.Id, c.Proposed.LicensePlate, c.Explanation)).ToList(),
            plan.Explanation);
    }
}
