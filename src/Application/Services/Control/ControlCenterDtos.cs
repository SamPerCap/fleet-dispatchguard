using DispatchGuard.Core.Enums;

namespace DispatchGuard.Application.Services.Control;

public enum ControlAlertType
{
    UnassignedTrip, PartialBooking, UnavailabilityGap, DriverNearLimit, VehicleUnavailable, ForcedAssignment, InsufficientSeats
}

/// <summary>Urgent when the deadline is less than 1 h away.</summary>
public enum ControlAlertSeverity { Urgent, Warning }

/// <summary>One alert per affected entity and type.</summary>
public sealed record ControlAlertDto(
    ControlAlertType Type, ControlAlertSeverity Severity, string EntityType, int EntityId, LocalizedText EntityLabel,
    DateTime? Deadline, LocalizedText SuggestedAction, LocalizedText? Detail);

/// <summary>
/// A null <c>BaseId</c> means every base. <c>UrgentAlerts</c> and <c>WarningAlerts</c> count grouped incidents, so
/// tasks = urgent + warnings (SPEC 0057 R4). In progress and completed are by schedule: there is no telemetry.
/// <c>NextDepartureLabel</c> is the name of the trip at <c>NextDeparture</c>.
/// </summary>
public sealed record ControlSummaryDto(
    int? BaseId, DateTime Date, string Light, LocalizedText Headline, LocalizedText Detail,
    int CoveredTrips, int TotalTrips, int UrgentAlerts, int WarningAlerts,
    int InProgressBySchedule = 0, int CompletedBySchedule = 0, int Upcoming = 0, DateTime? NextDeparture = null,
    int YesterdayTotalTrips = 0, int YesterdayCoveredTrips = 0, LocalizedText? NextDepartureLabel = null);

/// <summary>A trip affected by an incident.</summary>
public sealed record AffectedTripDto(int TripId, LocalizedText Label, DateTime Start, DateTime End, int Seats);

/// <summary>
/// Related alerts grouped into one (SPEC 0057 R2): worst severity, earliest deadline, impact (distinct affected trips and their
/// seats) and ONE recommended action, which acts on <c>EntityType</c>/<c>EntityId</c>.
/// </summary>
public sealed record ControlIncidentDto(
    string Key, ControlAlertSeverity Severity, ControlAlertType Type, string EntityType, int EntityId, LocalizedText EntityLabel,
    DateTime? Deadline, LocalizedText RecommendedAction, LocalizedText? Detail,
    int AffectedTripCount, int AffectedSeats, IReadOnlyList<AffectedTripDto> AffectedTrips, IReadOnlyList<ControlAlertType> Types);

public sealed record ControlPageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

/// <summary>
/// Active drivers right now, each in exactly one state (SPEC 0057 R5). Physical: in service (by schedule), unavailable. Legal: out
/// of hours (a driving limit reached), legally restricted (at 90 % of a limit or more). Assigned: a service later today.
/// </summary>
public sealed record DriverResourcesDto(int InService, int Assigned, int Available, int LegallyRestricted, int OutOfHours, int Unavailable)
{
    public int Total => InService + Assigned + Available + LegallyRestricted + OutOfHours + Unavailable;
}

/// <summary>Active vehicles right now, each in exactly one state (SPEC 0057 R5).</summary>
public sealed record VehicleResourcesDto(int InService, int Assigned, int Available, int Unavailable)
{
    public int Total => InService + Assigned + Available + Unavailable;
}

public sealed record ControlResourcesDto(int? BaseId, DateTime At, DriverResourcesDto Drivers, VehicleResourcesDto Vehicles);

/// <summary>
/// <c>Validations = Accepted + IllegalBlocked + Forced</c> always (SPEC 0057 R6). <c>Forced</c>: past CE 561/2006. Manual
/// assignments are always 0: nothing records them yet.
/// </summary>
public sealed record EngineMetricsDto(
    int? BaseId, DateTime From, DateTime To, int Validations, int IllegalBlocked,
    IReadOnlyDictionary<string, int> BlockedByReason, int ManualAssignments, int Forced = 0, int Accepted = 0);

/// <summary>One row of the activity feed, taken from the attempt log. <c>TripLabel</c> is the service's name.</summary>
public sealed record ActivityItemDto(
    int Id, DateTime At, string Who, int TripId, int DriverId, int VehicleId, string Outcome, LocalizedText? Detail, string? TripLabel = null,
    AttemptOrigin Origin = AttemptOrigin.Manual);

public enum ActivityKind { All, Assigned, Blocked, Forced }

public sealed record BaseDto(int Id, string Name);
