using DispatchGuard.Application.Services.Optimization;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;

namespace DispatchGuard.Application.Services.Control;

/// <summary>
/// Utilization over the fixed operating day of each base (<see cref="ControlEfficiency"/>) and the empty km of the current plan
/// against the proposed one (<see cref="VehicleChainingPlanner"/>, straight line). Departed trips keep their vehicle, so the
/// saving is only what can still change (SPEC 0070), and nobody leaves before now (SPEC 0071).
/// <c>WindowStart</c>/<c>WindowEnd</c>: the operating day when the whole scope shares it.
/// </summary>
public sealed record ControlEfficiencyDto(
    int? BaseId, DateTime From, DateTime To, DateTime? WindowStart, DateTime? WindowEnd,
    int ActiveVehicles, double AvailableVehicleHours, double AssignedVehicleHours, double FleetUtilizationPercent,
    IReadOnlyList<HourlyCountDto> VehiclesInServiceByHour,
    int TripsConsidered, int TripsDeparted, double BaselineDeadheadKm, double ProposedDeadheadKm, double SavingPercent, int ProposedChanges);

/// <summary>A null <c>baseId</c> means every base; an unknown base is a <see cref="KeyNotFoundException"/>.</summary>
public interface IControlInsightsService
{
    Task<ControlTrendDto> TrendsAsync(int? baseId, DateTime date, int days, CancellationToken ct = default);
    Task<EngineHourlyDto> EngineHourlyAsync(int? baseId, DateTime date, CancellationToken ct = default);
    Task<ControlEfficiencyDto> EfficiencyAsync(int? baseId, DateTime date, CancellationToken ct = default);
}

public sealed class ControlInsightsService : IControlInsightsService
{
    /// <summary>Same default as the optimizer endpoint: every day starts and ends at the base (SPEC 0043 R4).</summary>
    public const bool ReturnToBase = true;

    private readonly IControlCenterReadModel _read;
    private readonly ILocationRepository _locations;
    private readonly IVehicleEligibilityService _eligibility;
    private readonly OperatingHoursSettings _hours;
    private readonly IVehicleLastServiceReader _lastServices;
    private readonly TimeProvider _clock;

    public ControlInsightsService(IControlCenterReadModel read, ILocationRepository locations, IVehicleEligibilityService eligibility,
        OperatingHoursSettings hours, IVehicleLastServiceReader lastServices, TimeProvider clock)
    {
        _lastServices = lastServices ?? throw new ArgumentNullException(nameof(lastServices));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _hours = hours ?? throw new ArgumentNullException(nameof(hours));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
    }

    public async Task<ControlTrendDto> TrendsAsync(int? baseId, DateTime date, int days, CancellationToken ct = default)
    {
        var range = ControlTrends.Days(date, days);
        await EnsureBaseAsync(baseId, ct);
        var trips = await _read.TripsAsync(baseId, range[0].From, range[^1].To, ct);
        return new ControlTrendDto(baseId, ControlTrends.Daily(trips, range));
    }

    public async Task<EngineHourlyDto> EngineHourlyAsync(int? baseId, DateTime date, CancellationToken ct = default)
    {
        await EnsureBaseAsync(baseId, ct);
        var (from, to) = OperationalDay.Containing(date);
        var attempts = await _read.AttemptsAsync(baseId, from, to, ct);
        return new EngineHourlyDto(baseId, from, to, ControlTrends.Hourly(attempts, from, to));
    }

    public async Task<ControlEfficiencyDto> EfficiencyAsync(int? baseId, DateTime date, CancellationToken ct = default)
    {
        await EnsureBaseAsync(baseId, ct);
        var (from, to) = OperationalDay.Containing(date);
        var trips = await _read.TripsAsync(baseId, from, to, ct);
        var scope = await _read.VehiclesAsync(baseId, ct);
        var utilization = ControlEfficiency.Utilization(trips, scope, from, to, v => _hours.On(from, v.BaseId));

        // As in the optimizer, only trips that start this day (SPEC 0043 R5).
        var starting = trips.Where(t => t.ScheduledWindow.Start >= from && t.ScheduledWindow.Start < to).ToList();
        var fleet = scope.ToList();
        var missing = starting.Select(t => t.AssignedVehicleId).OfType<int>().Distinct().Except(fleet.Select(v => v.Id)).ToHashSet();
        if (missing.Count > 0) fleet.AddRange((await _read.VehiclesAsync(null, ct)).Where(v => missing.Contains(v.Id)));
        var locations = (await _locations.GetAllAsync(ct)).ToDictionary(l => l.Id);
        var now = _clock.GetUtcNow().UtcDateTime;
        var lastServices = await _lastServices.LastServicesAsync(fleet.Select(v => v.Id).ToList(), now, ct);
        var plan = VehicleChainingPlanner.Plan(starting, fleet, locations, ReturnToBase, _eligibility, now, lastServices);

        return new ControlEfficiencyDto(baseId, from, to, utilization.WindowStart, utilization.WindowEnd,
            utilization.ActiveVehicles, utilization.AvailableVehicleHours, utilization.AssignedVehicleHours, utilization.UtilizationPercent,
            utilization.VehiclesInServiceByHour,
            plan.TripsConsidered, plan.TripsDeparted, Math.Round(plan.BaselineDeadheadKm, 1), Math.Round(plan.ProposedDeadheadKm, 1),
            Math.Round(plan.SavingPercent, 1), plan.Changes.Count);
    }

    private async Task EnsureBaseAsync(int? baseId, CancellationToken ct)
    {
        if (baseId is { } id && !await _read.BaseExistsAsync(id, ct)) throw MissingResource.Base(id);
    }
}
