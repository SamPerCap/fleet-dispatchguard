using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Control;

/// <summary>
/// A null <c>baseId</c> means every base (SPEC 0057 R1). Days are operational days in Europe/Madrid
/// (<see cref="OperationalDay"/>).
/// </summary>
public interface IControlCenterService
{
    Task<IReadOnlyList<BaseDto>> BasesAsync(CancellationToken ct = default);
    Task<ControlSummaryDto> SummaryAsync(int? baseId, DateTime date, DateTime now, CancellationToken ct = default);
    Task<ControlPageDto<ControlAlertDto>> AlertsAsync(int? baseId, DateTime date, DateTime now, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Ordered by urgency and impact (SPEC 0057 R3).</summary>
    Task<IReadOnlyList<ControlIncidentDto>> IncidentsAsync(int? baseId, DateTime date, DateTime now, CancellationToken ct = default);
    Task<ControlResourcesDto> ResourcesAsync(int? baseId, DateTime at, CancellationToken ct = default);
    Task<EngineMetricsDto> EngineMetricsAsync(int? baseId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<ControlPageDto<ActivityItemDto>> ActivityAsync(int? baseId, DateTime since, int page, int pageSize, ActivityKind kind = ActivityKind.All, CancellationToken ct = default);
}

public class ControlCenterService : IControlCenterService
{
    public const int MaxPageSize = 100;

    /// <summary>For partially assigned bookings.</summary>
    public static readonly TimeSpan BookingHorizon = TimeSpan.FromDays(7);

    private readonly IControlCenterReadModel _read;
    private readonly IDriverHoursService _hours;

    public ControlCenterService(IControlCenterReadModel read, IDriverHoursService hours)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _hours = hours ?? throw new ArgumentNullException(nameof(hours));
    }

    public async Task<IReadOnlyList<BaseDto>> BasesAsync(CancellationToken ct = default) =>
        (await _read.BasesAsync(ct)).Select(b => new BaseDto(b.Id, b.Name)).ToList();

    public async Task<ControlSummaryDto> SummaryAsync(int? baseId, DateTime date, DateTime now, CancellationToken ct = default)
    {
        await EnsureBaseAsync(baseId, ct);
        var (from, to) = OperationalDay.Containing(date);
        var trips = await _read.TripsAsync(baseId, from, to, ct);
        var incidents = ControlIncidents.Group(await BuildAlertsAsync(baseId, now, trips, ct));

        // One source for every figure, so that tasks = incidents = urgent + warnings (SPEC 0057 R4).
        var urgent = incidents.Count(i => i.Severity == ControlAlertSeverity.Urgent);
        var warnings = incidents.Count - urgent;
        var light = ControlRules.Light(urgent, warnings);
        var covered = trips.Count(ControlRules.IsCovered);
        var (headline, detail) = ControlRules.Wording(urgent, warnings, covered, trips.Count);
        var upcoming = trips.Where(t => t.ScheduledWindow.Start > now).OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToList();

        var (yFrom, yTo) = OperationalDay.Before(date);
        var yesterday = await _read.TripsAsync(baseId, yFrom, yTo, ct);

        return new ControlSummaryDto(baseId, from, light.ToString(), headline, detail, covered, trips.Count, urgent, warnings,
            InProgressBySchedule: trips.Count(t => ControlRules.InProgressBySchedule(t, now)),
            CompletedBySchedule: trips.Count(t => ControlRules.CompletedBySchedule(t, now)),
            Upcoming: upcoming.Count,
            NextDeparture: upcoming.FirstOrDefault()?.ScheduledWindow.Start,
            YesterdayTotalTrips: yesterday.Count,
            YesterdayCoveredTrips: yesterday.Count(ControlRules.IsCovered),
            NextDepartureLabel: upcoming.FirstOrDefault() is { } next ? Label(next) : null);
    }

    public async Task<ControlPageDto<ControlAlertDto>> AlertsAsync(int? baseId, DateTime date, DateTime now, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize);
        await EnsureBaseAsync(baseId, ct);
        var (from, to) = OperationalDay.Containing(date);
        var trips = await _read.TripsAsync(baseId, from, to, ct);
        var alerts = (await BuildAlertsAsync(baseId, now, trips, ct)).Select(f => f.Alert)
            .OrderBy(a => a.Severity)
            .ThenBy(a => a.Deadline ?? DateTime.MaxValue)
            .ThenBy(a => a.Type).ThenBy(a => a.EntityId)
            .ToList();
        return new ControlPageDto<ControlAlertDto>(
            alerts.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, alerts.Count, (alerts.Count + pageSize - 1) / pageSize);
    }

    public async Task<IReadOnlyList<ControlIncidentDto>> IncidentsAsync(int? baseId, DateTime date, DateTime now, CancellationToken ct = default)
    {
        await EnsureBaseAsync(baseId, ct);
        var (from, to) = OperationalDay.Containing(date);
        var trips = await _read.TripsAsync(baseId, from, to, ct);
        return ControlIncidents.Group(await BuildAlertsAsync(baseId, now, trips, ct));
    }

    public async Task<ControlResourcesDto> ResourcesAsync(int? baseId, DateTime at, CancellationToken ct = default)
    {
        await EnsureBaseAsync(baseId, ct);
        var instant = Instant(at);
        var (_, dayEnd) = OperationalDay.Containing(at);
        // Covered trips that may be running now (they can start up to a day before) or start later today.
        var trips = (await _read.TripsAsync(baseId, at.AddDays(-1), dayEnd, ct)).Where(ControlRules.IsCovered).ToList();
        var running = trips.Where(t => ControlRules.InProgressBySchedule(t, at)).ToList();
        var later = trips.Where(t => t.ScheduledWindow.Start > at).ToList();
        var driversNow = running.Select(t => t.AssignedDriverId!.Value).ToHashSet();
        var driversLater = later.Select(t => t.AssignedDriverId!.Value).ToHashSet();
        var vehiclesNow = running.Select(t => t.AssignedVehicleId!.Value).ToHashSet();
        var vehiclesLater = later.Select(t => t.AssignedVehicleId!.Value).ToHashSet();

        // Each active resource counts in exactly one state, by precedence (SPEC 0057 R5).
        int inService = 0, assigned = 0, available = 0, restricted = 0, outOfHours = 0, unavailable = 0;
        foreach (var driver in (await _read.DriversAsync(baseId, ct)).Where(d => d.IsActive))
        {
            if (driversNow.Contains(driver.Id)) { inService++; continue; }
            if (!driver.IsAvailableDuring(instant)) { unavailable++; continue; }
            var hours = await _hours.MeasureAsync(driver.Id, at, ct);
            if (ReachedAnyLimit(hours)) outOfHours++;
            else if (driversLater.Contains(driver.Id)) assigned++;
            else if (ControlRules.LimitsNear(hours).Count > 0) restricted++;
            else available++;
        }

        int vInService = 0, vAssigned = 0, vAvailable = 0, vUnavailable = 0;
        foreach (var vehicle in (await _read.VehiclesAsync(baseId, ct)).Where(v => v.IsActive))
        {
            if (vehiclesNow.Contains(vehicle.Id)) vInService++;
            else if (!vehicle.IsAvailableDuring(instant)) vUnavailable++;
            else if (vehiclesLater.Contains(vehicle.Id)) vAssigned++;
            else vAvailable++;
        }

        return new ControlResourcesDto(baseId, at,
            new DriverResourcesDto(inService, assigned, available, restricted, outOfHours, unavailable),
            new VehicleResourcesDto(vInService, vAssigned, vAvailable, vUnavailable));
    }

    public async Task<EngineMetricsDto> EngineMetricsAsync(int? baseId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to <= from) throw new InvalidRequestException(Texts.FieldAfter(nameof(to), nameof(from)));
        await EnsureBaseAsync(baseId, ct);
        var attempts = await _read.AttemptsAsync(baseId, from, to, ct);
        var byReason = attempts.Where(a => a.IsRejected)
            .GroupBy(a => a.Outcome.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        return new EngineMetricsDto(baseId, from, to, attempts.Count, byReason.Values.Sum(), byReason, ManualAssignments: 0,
            Forced: attempts.Count(a => a.Outcome == AllocationOutcome.Forced),
            Accepted: attempts.Count(a => a.Outcome == AllocationOutcome.Assigned));
    }

    /// <summary>Null = every outcome.</summary>
    public static IReadOnlyCollection<AllocationOutcome>? OutcomesOf(ActivityKind kind) => kind switch
    {
        ActivityKind.Assigned => [AllocationOutcome.Assigned],
        ActivityKind.Forced => [AllocationOutcome.Forced],
        ActivityKind.Blocked => Enum.GetValues<AllocationOutcome>()
            .Where(o => o is not (AllocationOutcome.Assigned or AllocationOutcome.Forced)).ToArray(),
        _ => null
    };

    public async Task<ControlPageDto<ActivityItemDto>> ActivityAsync(int? baseId, DateTime since, int page, int pageSize, ActivityKind kind = ActivityKind.All, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize);
        if (!Enum.IsDefined(kind)) throw new InvalidRequestException(Texts.UnknownValue(nameof(kind), kind.ToString()));
        await EnsureBaseAsync(baseId, ct);
        var result = await _read.ActivityAsync(baseId, since, OutcomesOf(kind), page, pageSize, ct);
        var labels = await _read.TripLabelsAsync(result.Items.Select(a => a.TripId).Distinct().ToList(), ct);
        var items = result.Items
            .Select(a => new ActivityItemDto(a.Id, a.AttemptedAt, a.AttemptedBy, a.TripId, a.DriverId, a.VehicleId, a.Outcome.ToString(), AttemptTexts.Of(a).Detail,
                labels.TryGetValue(a.TripId, out var label) ? label : null, a.Origin))
            .ToList();
        return new ControlPageDto<ActivityItemDto>(items, result.Page, result.PageSize, result.TotalCount, result.TotalPages);
    }

    private async Task<List<AlertFact>> BuildAlertsAsync(int? baseId, DateTime now, IReadOnlyList<Trip> dayTrips, CancellationToken ct)
    {
        var facts = new List<AlertFact>();
        // Only what still affects a trip that has not ended is actionable (SPEC 0057 R2).
        bool Ahead(Trip trip) => trip.ScheduledWindow.End > now;

        foreach (var trip in dayTrips.Where(t => t.Status is TripStatus.Pending or TripStatus.ValidationFailed).Where(Ahead))
        {
            var start = trip.ScheduledWindow.Start;
            facts.Add(new AlertFact(
                new ControlAlertDto(ControlAlertType.UnassignedTrip, SeverityFor(start, now), "Trip", trip.Id,
                    Label(trip), start, Texts.ActionAssign(), null),
                AlertFact.BookingKey(trip.BookingId), [Affected(trip)]));
        }

        foreach (var booking in await _read.BookingsAsync(baseId, now, now + BookingHorizon, ct))
        {
            if (booking.AssignmentStatus is not (BookingAssignmentStatus.PartiallyAssigned or BookingAssignmentStatus.Unassigned)) continue;
            var live = booking.Trips.Where(t => t.IsActive && t.Status != TripStatus.Cancelled).ToList();
            var pending = live.Where(t => !ControlRules.IsCovered(t)).ToList();
            if (!pending.Any(Ahead)) continue;
            facts.Add(new AlertFact(
                new ControlAlertDto(ControlAlertType.PartialBooking, ControlAlertSeverity.Warning, "Booking", booking.Id,
                    Texts.AsWritten(booking.Description), pending.Where(Ahead).Min(t => (DateTime?)t.ScheduledWindow.Start), Texts.ActionCompleteBooking(),
                    Texts.TripsCovered(live.Count - pending.Count, live.Count)),
                AlertFact.BookingKey(booking.Id), pending.Where(Ahead).Select(Affected).ToList()));
        }

        var assigned = dayTrips.Where(ControlRules.IsCovered).Where(Ahead).ToList();

        // A forced assignment is a warning, never hidden (SPEC 0056 R6), and goes with its driver (SPEC 0057 R2).
        foreach (var trip in assigned.Where(t => t.IsForced))
        {
            facts.Add(new AlertFact(
                new ControlAlertDto(ControlAlertType.ForcedAssignment, ControlAlertSeverity.Warning, "Trip", trip.Id,
                    Label(trip), trip.ScheduledWindow.Start, Texts.ActionReviewForced(),
                    trip.ForcedDescription),
                AlertFact.DriverKey(trip.AssignedDriverId!.Value), [Affected(trip)]));
        }

        foreach (var driver in await _read.DriversAsync(baseId, ct))
        {
            var mine = assigned.Where(t => t.AssignedDriverId == driver.Id).ToList();
            if (mine.Count == 0) continue;

            // The same check as allocating, so an inactive driver is a gap too (SPEC 0066 R1, R7).
            var gaps = mine.Where(t => !DriverEligibilityCheck.For(driver, t.ScheduledWindow).IsEligible).ToList();
            if (gaps.Count > 0)
            {
                var gap = gaps[0];
                facts.Add(new AlertFact(
                    new ControlAlertDto(ControlAlertType.UnavailabilityGap, SeverityFor(gap.ScheduledWindow.Start, now), "Driver", driver.Id,
                        Texts.AsWritten(driver.FullName), gap.ScheduledWindow.Start, Texts.ActionFindReplacement(), Texts.NotAvailableForService(gap.Id)),
                    AlertFact.DriverKey(driver.Id), gaps.Select(Affected).ToList()));
                continue; // One alert per entity.
            }

            var near = ControlRules.LimitsNear(await _hours.MeasureAsync(driver.Id, now, ct));
            if (near.Count > 0)
            {
                facts.Add(new AlertFact(
                    new ControlAlertDto(ControlAlertType.DriverNearLimit, ControlAlertSeverity.Warning, "Driver", driver.Id,
                        Texts.AsWritten(driver.FullName), mine.Min(t => t.ScheduledWindow.Start), Texts.ActionReviewNextServices(),
                        Texts.NearLimits(ControlRules.NearLimitPercent, near.Select(LimitName).ToList())),
                    AlertFact.DriverKey(driver.Id), mine.Select(Affected).ToList()));
            }
        }

        foreach (var vehicle in await _read.VehiclesAsync(baseId, ct))
        {
            var mine = assigned.Where(t => t.AssignedVehicleId == vehicle.Id).ToList();
            if (mine.Count == 0) continue;
            var first = mine.Min(t => t.ScheduledWindow.Start);

            // Besides its unavailability, never instead of it: each may be about a different service (SPEC 0081).
            if (SeatsAlert(vehicle, mine, now) is { } seats) facts.Add(seats);

            if (!vehicle.IsAvailableDuring(Instant(now)))
            {
                facts.Add(new AlertFact(
                    new ControlAlertDto(ControlAlertType.VehicleUnavailable, SeverityFor(first, now), "Vehicle", vehicle.Id,
                        Texts.AsWritten(vehicle.LicensePlate), first, Texts.ActionChangeVehicle(), Texts.PendingToday(mine.Count)),
                    AlertFact.VehicleKey(vehicle.Id), mine.Select(Affected).ToList()));
                continue;
            }

            var gaps = mine.Where(t => vehicle.Unavailabilities.Any(u => u.OverlapsWith(t.ScheduledWindow))).ToList();
            if (gaps.Count > 0)
            {
                var gap = gaps[0];
                facts.Add(new AlertFact(
                    new ControlAlertDto(ControlAlertType.UnavailabilityGap, SeverityFor(gap.ScheduledWindow.Start, now), "Vehicle", vehicle.Id,
                        Texts.AsWritten(vehicle.LicensePlate), gap.ScheduledWindow.Start, Texts.ActionChangeVehicle(), Texts.NotAvailableForService(gap.Id)),
                    AlertFact.VehicleKey(vehicle.Id), gaps.Select(Affected).ToList()));
            }
        }

        return facts;
    }

    /// <summary>Seats can be lowered over services already assigned, which stay (SPEC 0079).</summary>
    private static AlertFact? SeatsAlert(Vehicle vehicle, IReadOnlyList<Trip> assigned, DateTime now)
    {
        var tooBig = assigned.Where(t => !vehicle.HasCapacityFor(t.RequiredSeats)).OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToList();
        if (tooBig.Count == 0) return null;

        var detail = tooBig.Count == 1
            ? Texts.SeatsShortForOne(vehicle.SeatingCapacity, tooBig[0].Id, tooBig[0].RequiredSeats)
            : Texts.SeatsShortForMany(vehicle.SeatingCapacity, tooBig.Count, tooBig.Max(t => t.RequiredSeats));
        var departure = tooBig[0].ScheduledWindow.Start;
        return new AlertFact(
            new ControlAlertDto(ControlAlertType.InsufficientSeats, SeverityFor(departure, now), "Vehicle", vehicle.Id,
                Texts.AsWritten(vehicle.LicensePlate), departure, Texts.ActionChangeVehicle(), detail),
            AlertFact.VehicleKey(vehicle.Id), tooBig.Select(Affected).ToList());
    }

    private static LocalizedText Label(Trip trip) => trip.Description is { } description ? Texts.AsWritten(description) : Texts.ServiceLabel(trip.Id);

    private static AffectedTripDto Affected(Trip trip) =>
        new(trip.Id, Label(trip), trip.ScheduledWindow.Start, trip.ScheduledWindow.End, trip.RequiredSeats);

    private static TimeWindow Instant(DateTime at) => new(at, at.AddMinutes(1));

    private static ControlAlertSeverity SeverityFor(DateTime departure, DateTime now) =>
        ControlRules.IsUrgent(departure, now) ? ControlAlertSeverity.Urgent : ControlAlertSeverity.Warning;

    private static bool ReachedAnyLimit(DriverHours hours) =>
        hours.Today >= DrivingLimits.MaxDailyDriving || hours.ThisWeek >= DrivingLimits.MaxWeeklyDriving || hours.LastTwoWeeks >= DrivingLimits.MaxBiWeeklyDriving;

    private static LocalizedText LimitName(DrivingLimitKind kind) => kind switch
    {
        DrivingLimitKind.Daily => Texts.DailyLimit(),
        DrivingLimitKind.Weekly => Texts.WeeklyLimit(),
        _ => Texts.TwoWeeksLimit()
    };

    private async Task EnsureBaseAsync(int? baseId, CancellationToken ct)
    {
        if (baseId is { } id && !await _read.BaseExistsAsync(id, ct)) throw MissingResource.Base(id);
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw new InvalidRequestException(Texts.FieldAtLeast(nameof(page), 1));
        if (pageSize < 1 || pageSize > MaxPageSize) throw new InvalidRequestException(Texts.FieldBetween(nameof(pageSize), 1, MaxPageSize));
    }
}
