// API contracts (PascalCase, see src/Application/DTOs).
export interface Trip {
  Id: number;
  OriginLocationId: number;
  DestinationLocationId: number;
  Start: string;
  End: string;
  RequiredSeats: number;
  ServiceType: string;
  Status: string;
  Description: string | null;
  AssignedDriverId: number | null;
  AssignedVehicleId: number | null;
  IsLocked: boolean;
  IsActive: boolean;
  BookingId: number;
  BaseId?: number;
  /** Written by the API: forced by itself or by an earlier service. */
  ForcedDescription?: string | null;
}
export interface Unavailability {
  Id: number;
  Start: string;
  End: string;
  Type: string;
  Notes: string | null;
  /** Workshop; only of vehicles. */
  ServiceProvider?: string | null;
}
/** An assigned service that a change of its driver or vehicle affects. */
export interface AffectedTrip {
  TripId: number;
  BookingId: number;
  CustomerName: string;
  Start: string;
  End: string;
  Status: string;
  OriginLocationId: number;
  DestinationLocationId: number;
  AssignedDriverId: number | null;
  AssignedVehicleId: number | null;
  RequiredSeats: number;
}
export interface Driver {
  Id: number;
  IdentificationNumber: string;
  FirstName: string;
  LastName: string;
  IsExternal: boolean;
  AllowsAutoAssignment: boolean;
  IsActive: boolean;
  HomeLocationId: number | null;
  Unavailabilities: Unavailability[];
  /** Goes back in the save: the API refuses it if somebody else changed the data meanwhile (SPEC 0090). */
  DetailsVersion: string;
  BaseId?: number;
}
export interface Vehicle {
  Id: number;
  LicensePlate: string;
  Brand: string;
  Model: string;
  SeatingCapacity: number;
  IsExternal: boolean;
  AllowsAutoAssignment: boolean;
  IsActive: boolean;
  HomeLocationId: number | null;
  Unavailabilities: Unavailability[];
  /** Goes back in the save: the API refuses it if somebody else changed the data meanwhile (SPEC 0090). */
  DetailsVersion: string;
  BaseId?: number;
}
export interface Location {
  Id: number;
  Name: string;
  Address: string;
  City: string;
  Province: string;
}
export type ExplanationOutcome = 'Passed' | 'Warning' | 'Failed' | 'NotEvaluated';
export interface ExplanationItem {
  /** Stable; the title is in the language of the reader. Missing in attempts recorded before SPEC 0095. */
  Code?: string;
  Title: string;
  Reference: string | null;
  Outcome: ExplanationOutcome;
  Detail: string;
}
export interface TechnicalStep {
  Component: string;
  Role: string;
  Outcome: ExplanationOutcome;
  Result: string;
}
export interface Explanation {
  Summary: string;
  Business: ExplanationItem[];
  Flow: TechnicalStep[];
  Patterns: string[];
  Adrs: string[];
}
export interface DriverCandidate {
  Rank: number;
  DriverId: number;
  FullName: string;
  Proximity: string;
  DistanceKm: number | null;
  Reason: string;
  Warnings: string[];
  Explanation?: Explanation | null;
  CoversSameBooking?: boolean;
  HoursSinceWeeklyRest?: number | null;
  WonTieBreak?: boolean;
}
export interface DriverOptions {
  Candidates: DriverCandidate[];
  /** `Broken`: the rules of CE 561/2006, when that is the `Cause`. */
  Discarded: { DriverId: number; FullName: string; Reason: string; Cause?: string; Broken?: string | null }[];
}
export interface DriverHours {
  DriverId: number;
  At: string;
  Today: number;
  ThisWeek: number;
  LastTwoWeeks: number;
  SinceLastWeeklyRest: number;
  LastWeeklyRestEnd: string | null;
  MaxDaily: number;
  MaxWeekly: number;
  MaxBiWeekly: number;
  /** The rest reaches the queried instant: by schedule, it has not ended. */
  WeeklyRestInProgress: boolean;
}
export interface BookingTrip {
  Id: number;
  OriginLocationId: number;
  DestinationLocationId: number;
  Start: string;
  End: string;
  RequiredSeats: number;
  ServiceType: string;
  Status: string;
  Description: string | null;
  AssignedDriverId: number | null;
  AssignedDriverName: string | null;
  AssignedVehicleId: number | null;
  AssignedVehiclePlate: string | null;
  IsLocked: boolean;
  ForcedDescription?: string | null;
}
export interface Booking {
  Id: number;
  CustomerName: string;
  Description: string;
  Notes: string | null;
  AssignmentStatus: string;
  Start: string | null;
  End: string | null;
  IsActive: boolean;
  Trips: BookingTrip[];
}
export interface NewTrip {
  OriginLocationId: number;
  DestinationLocationId: number;
  Start: string;
  End: string;
  RequiredSeats: number;
  ServiceType: string;
  Description?: string | null;
}
export interface VehicleCandidate {
  Rank: number;
  VehicleId: number;
  LicensePlate: string;
  SeatingCapacity: number;
  Proximity: string;
  PositionSource: string;
  DistanceKm: number | null;
  Reason: string;
  Explanation?: Explanation | null;
}
export interface AllocationResponse {
  TripId: number;
  DriverId: number;
  VehicleId: number;
  Status: string;
  Forced?: boolean;
  ForcedReason?: string | null;
  Explanation?: Explanation | null;
}
/** Attempts from before this field existed are "Manual". */
export type AttemptOrigin = 'Manual' | 'Recommended';
export interface RecommendedResource {
  Id: number;
  Name: string;
  DistanceKm: number | null;
  StepKm: number | null;
  Where: string;
  SameBooking: boolean;
  Late: string | null;
}
export interface RecommendationLine {
  TripId: number;
  BookingId: number;
  CustomerName: string;
  BaseId: number;
  Start: string;
  End: string;
  Origin: string;
  Destination: string;
  RequiredSeats: number;
  Status: 'Proposed' | 'NoSolution' | 'Invalid';
  Driver: RecommendedResource | null;
  Vehicle: RecommendedResource | null;
  Reason: string;
  Warnings: string[];
  Explanation: Explanation | null;
}
export interface Recommendation {
  CalculatedAt: string;
  Lines: RecommendationLine[];
  LeftOut: number;
  MaxTrips: number;
  MaxDays: number;
  StepsKm: number[];
  Note: string;
  ArrivalNote: string;
}
export interface RecommendationRequest {
  BookingId?: number;
  BaseId?: number | null;
  From?: string;
  To?: string;
}
export interface RecommendationLineInput {
  TripId: number;
  DriverId: number | null;
  VehicleId: number | null;
  Manual: boolean;
}
export interface RecommendationOptions {
  Drivers: RecommendedResource[];
  Vehicles: RecommendedResource[];
}
export interface TripRecommendation {
  TripId: number;
  CalculatedAt: string;
  DriverId: number | null;
  VehicleId: number | null;
}
/** A later service of the driver that would break (or break further) a rule. */
export interface LaterService {
  TripId: number | null;
  Start: string;
  Article: string;
  Rule: string;
  Message: string;
}
export interface Paged<T> {
  Items: T[];
  TotalCount: number;
  Page: number;
  PageSize: number;
}
export interface TimeWindow {
  start: Date;
  end: Date;
}

/** `TripsDeparted`: those that have already left and keep their bus. */
export interface VehicleChange {
  TripId: number;
  Start: string;
  End: string;
  DriverId: number;
  CurrentVehicleId: number;
  CurrentLicensePlate: string;
  ProposedVehicleId: number;
  ProposedLicensePlate: string;
  Explanation: Explanation;
}
export interface VehicleChaining {
  From: string;
  To: string;
  ReturnToBase: boolean;
  AverageSpeedKmh: number;
  TripsConsidered: number;
  TripsDeparted: number;
  BaselineDeadheadKm: number;
  ProposedDeadheadKm: number;
  SavingPercent: number;
  Changes: VehicleChange[];
  Explanation: Explanation;
}
export interface OperatingBase {
  Id: number;
  Name: string;
}
export type AlertType =
  | 'UnassignedTrip'
  | 'PartialBooking'
  | 'UnavailabilityGap'
  | 'DriverNearLimit'
  | 'VehicleUnavailable'
  | 'ForcedAssignment'
  | 'InsufficientSeats';
export type Severity = 'Urgent' | 'Warning';
export interface AffectedTrip {
  TripId: number;
  Label: string;
  Start: string;
  End: string;
  Seats: number;
}
/** Related alerts grouped, with ONE recommended action on EntityType/EntityId. */
export interface ControlIncident {
  Key: string;
  Severity: Severity;
  Type: AlertType;
  EntityType: string;
  EntityId: number;
  EntityLabel: string;
  Deadline: string | null;
  RecommendedAction: string;
  Detail: string | null;
  AffectedTripCount: number;
  AffectedSeats: number;
  AffectedTrips: AffectedTrip[];
  Types: AlertType[];
}
export interface ControlSummary {
  BaseId: number | null;
  Date: string;
  Light: 'Green' | 'Amber' | 'Red' | string;
  Headline: string;
  Detail: string;
  CoveredTrips: number;
  TotalTrips: number;
  UrgentAlerts: number;
  WarningAlerts: number;
  InProgressBySchedule: number;
  CompletedBySchedule: number;
  Upcoming: number;
  NextDeparture: string | null;
  YesterdayTotalTrips: number;
  YesterdayCoveredTrips: number;
  NextDepartureLabel?: string | null;
}
export interface ControlPage<T> {
  Items: T[];
  Page: number;
  PageSize: number;
  TotalCount: number;
  TotalPages: number;
}
/** Each active resource is in a single state; physical (in service, unavailable) against legal (no hours left, restricted). */
export interface ControlResources {
  BaseId: number | null;
  At: string;
  Drivers: {
    InService: number;
    Assigned: number;
    Available: number;
    LegallyRestricted: number;
    OutOfHours: number;
    Unavailable: number;
    Total: number;
  };
  Vehicles: { InService: number; Assigned: number; Available: number; Unavailable: number; Total: number };
}
/** Validations = Accepted + IllegalBlocked + Forced. */
export interface EngineMetrics {
  BaseId: number | null;
  From: string;
  To: string;
  Validations: number;
  IllegalBlocked: number;
  BlockedByReason: Record<string, number>;
  ManualAssignments: number;
  Forced: number;
  Accepted: number;
}
export interface ActivityItem {
  Id: number;
  At: string;
  Who: string;
  TripId: number;
  DriverId: number;
  VehicleId: number;
  Outcome: string;
  Detail: string | null;
  TripLabel?: string | null;
  Origin?: AttemptOrigin;
}
export type ActivityKind = 'All' | 'Assigned' | 'Blocked' | 'Forced';

/** From the oldest day to today: the last one equals the summary. */
export interface ControlTrend {
  BaseId: number | null;
  Days: { Date: string; TotalTrips: number; CoveredTrips: number }[];
}
/** Validations = Accepted + Blocked + Forced, in every hour. */
export interface EngineHour {
  Hour: string;
  Validations: number;
  Accepted: number;
  Blocked: number;
  Forced: number;
}
export interface EngineHourly {
  BaseId: number | null;
  From: string;
  To: string;
  Hours: EngineHour[];
}
export interface ControlEfficiency {
  BaseId: number | null;
  From: string;
  To: string;
  WindowStart: string | null;
  WindowEnd: string | null;
  ActiveVehicles: number;
  AvailableVehicleHours: number;
  AssignedVehicleHours: number;
  FleetUtilizationPercent: number;
  VehiclesInServiceByHour: { Hour: string; Count: number }[];
  TripsConsidered: number;
  TripsDeparted: number;
  BaselineDeadheadKm: number;
  ProposedDeadheadKm: number;
  SavingPercent: number;
  ProposedChanges: number;
}

export type RuleKind = 'Blocking' | 'Warning' | 'Ranking' | 'Informative';
export interface Rule {
  Id: string;
  Name: string;
  Reference: string;
  Explanation: string;
  Figure: string;
  Kind: RuleKind;
}
export interface RuleGroup {
  Key: string;
  Title: string;
  Rules: Rule[];
}

/** Validations = Accepted + Blocked + Forced; each one = rows of the audit log with that outcome. */
export interface EngineCounts {
  Validations: number;
  Accepted: number;
  Blocked: number;
  Forced: number;
}
/** `RuleIds` are catalogue ids (`/api/rules`). */
export interface EngineAttempt {
  Id: number;
  At: string;
  Who: string;
  BaseId: number;
  TripId: number;
  TripLabel: string | null;
  DriverId: number;
  VehicleId: number;
  Outcome: string;
  Detail: string | null;
  RuleIds: string[];
  ForcedReason: string | null;
  HasExplanation: boolean;
  Origin?: AttemptOrigin;
  /** Start of the anonymous session that made it; `Who` is only declared. */
  Session?: string | null;
}
export interface EngineAuditPage {
  BaseId: number | null;
  From: string;
  To: string;
  Counts: EngineCounts;
  Items: EngineAttempt[];
  Page: number;
  PageSize: number;
  TotalCount: number;
  TotalPages: number;
  People: string[];
  Rules: string[];
}
export interface EngineAttemptDetail {
  Attempt: EngineAttempt;
  Explanation: Explanation | null;
}

/** `Date` = UTC instant when the Madrid day starts. */
export interface HistoryDay {
  Date: string;
  Hours: number;
  Trips: number;
  Km: number;
}
export interface HistoryTrip {
  TripId: number;
  BookingId: number;
  Start: string;
  End: string;
  Hours: number;
  OriginLocationId: number;
  DestinationLocationId: number;
  Description: string | null;
  ServiceType: string;
  DriverId: number | null;
  VehicleId: number | null;
  Km: number | null;
  ForcedDescription: string | null;
}
/** Limits and number of days come from the API (`DrivingLimits`, `ResourceHistory.Days`). */
export interface ResourceHistory {
  Resource: 'Driver' | 'Vehicle';
  ResourceId: number;
  At: string;
  From: string;
  To: string;
  DaysCount: number;
  DailyLimitHours: number;
  ExtendedDailyLimitHours: number;
  MaxExtendedDaysPerWeek: number;
  Days: HistoryDay[];
  Totals: {
    Hours: number;
    Trips: number;
    Km: number;
    TripsWithoutKm: number;
    DaysWorked: number;
    DaysOverDaily: number;
    DaysOverExtended: number;
  };
  Items: HistoryTrip[];
  Page: number;
  PageSize: number;
  TotalCount: number;
  TotalPages: number;
}

/** `Changes` is the JSON of the audited fields; null if it is not a change. */
export interface TimelineEntry {
  At: string;
  Kind: string;
  Name: string;
  TraceId: string | null;
  Status: number | null;
  DurationMs: number | null;
  RequestId: number | null;
  EntityId: number | null;
  Changes: string | null;
  DeclaredName: string | null;
}

export interface VisitTimeline {
  SessionId: string;
  StartedAt: string | null;
  Reference: string | null;
  Entries: TimelineEntry[];
}

export interface VisitSummary {
  SessionId: string;
  StartedAt: string;
  LastAt: string;
  Reference: string | null;
  Events: number;
  Changes: number;
}

export interface RequestTraceDetail {
  Id: number;
  DurationMs: number;
  Queries: number;
  QueriesMs: number;
  Detail: {
    Rules: { Id: string; Passed: number; Failed: number }[];
    Queries: { Sql: string; Times: number; Milliseconds: number }[];
  };
}
