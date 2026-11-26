import { t } from './i18n/t';
// The backend decides legality, severity, grouping and order of the incidents; here they are only placed and worded.
import { formatTime, parseApiDate } from './gantt';
import { REFRESH_MS } from './refresh';
import { isForced, tripStatus, type StatusKey } from './status';
import type { ActivityItem, ControlIncident, Trip } from './types';

export const STRIP_HOURS = 4;
export const STRIP_MIN_HOURS = 1;
export const STRIP_MAX_HOURS = 16;
const HOUR = 3_600_000;

export const STALE_AFTER_MS = 2 * REFRESH_MS;

export function clampStripHours(value: unknown): number {
  const parsed = typeof value === 'number' ? value : typeof value === 'string' && value.trim() !== '' ? Number(value) : NaN;
  if (!Number.isFinite(parsed)) return STRIP_HOURS;
  return Math.min(STRIP_MAX_HOURS, Math.max(STRIP_MIN_HOURS, Math.round(parsed)));
}

/** If the last regular mark is less than half a step from the end it is dropped, so that the labels do not overlap. */
export function stripTicks(hours: number): number[] {
  const step = hours <= 6 ? 1 : hours <= 12 ? 2 : 4;
  const out: number[] = [];
  for (let hour = 0; hour < hours; hour += step) out.push(hour);
  if (out.length > 1 && hours - out[out.length - 1] < step / 2) out.pop();
  out.push(hours);
  return out;
}

export function stripWindow(now: Date, hours = STRIP_HOURS): { from: Date; to: Date } {
  const from = new Date(now);
  from.setMinutes(0, 0, 0);
  return { from, to: new Date(from.getTime() + hours * HOUR) };
}

export interface StripBlock {
  trip: Trip;
  left: number;
  width: number;
  status: StatusKey;
  forced: boolean;
  /** The service starts before / ends after the window (straight edge, without a misleading "cut"). */
  clipStart: boolean;
  clipEnd: boolean;
}

export function stripBlocks(trips: Trip[], from: Date, to: Date, now: Date): StripBlock[] {
  const span = to.getTime() - from.getTime();
  return trips
    .filter(t => t.IsActive)
    .map(trip => {
      const status = tripStatus(trip, now);
      if (!status) return null;
      const start = parseApiDate(trip.Start).getTime(),
        end = parseApiDate(trip.End).getTime();
      const clippedStart = Math.max(start, from.getTime()),
        clippedEnd = Math.min(end, to.getTime());
      if (clippedEnd <= clippedStart) return null;
      return {
        trip,
        status,
        forced: isForced(trip),
        left: ((clippedStart - from.getTime()) / span) * 100,
        width: ((clippedEnd - clippedStart) / span) * 100,
        clipStart: start < from.getTime(),
        clipEnd: end > to.getTime(),
      };
    })
    .filter((b): b is StripBlock => b !== null)
    .sort((a, b) => a.left - b.left || a.trip.Id - b.trip.Id);
}

export function lanes(blocks: StripBlock[]): StripBlock[][] {
  const out: StripBlock[][] = [];
  for (const block of blocks) {
    const lane = out.find(laneBlocks => {
      const last = laneBlocks[laneBlocks.length - 1];
      return last.left + last.width <= block.left + 1e-9;
    });
    if (lane) lane.push(block);
    else out.push([block]);
  }
  return out;
}

export type StripMode = 'driver' | 'vehicle' | 'service';
export interface StripRow {
  key: string;
  label: string;
  sub?: string;
  missing: boolean;
  blocks: StripBlock[];
}

/** If two blocks of the same resource overlap, lanes are opened: a row never has overlaps. */
export function stripRows(blocks: StripBlock[], mode: StripMode, name: (id: number) => string): StripRow[] {
  if (mode === 'service')
    return blocks.map(block => ({
      key: `t${block.trip.Id}`,
      label: block.trip.Description ?? t.common.serviceOf(block.trip.Id),
      sub: `${formatTime(parseApiDate(block.trip.Start))}–${formatTime(parseApiDate(block.trip.End))}`,
      missing: false,
      blocks: [block],
    }));

  const idOf = (block: StripBlock) => (mode === 'driver' ? block.trip.AssignedDriverId : block.trip.AssignedVehicleId);
  const groups = new Map<number | null, StripBlock[]>();
  for (const block of blocks) {
    const id = idOf(block);
    groups.set(id, [...(groups.get(id) ?? []), block]);
  }
  const missingLabel = mode === 'driver' ? t.common.noDriver : t.common.noVehicle;
  const keys = [...groups.keys()].sort((first, second) =>
    first === null ? -1 : second === null ? 1 : name(first).localeCompare(name(second), 'es') || first - second,
  );
  return keys.flatMap(id =>
    lanes(groups.get(id)!).map((lane, index) => ({
      key: `${mode}-${id ?? 'none'}-${index}`,
      label: index === 0 ? (id === null ? missingLabel : name(id)) : '',
      missing: id === null,
      blocks: lane,
    })),
  );
}

export type AlertTarget =
  | { kind: 'trip'; tripId: number }
  | { kind: 'booking'; bookingId: number }
  | { kind: 'driver'; id: number }
  | { kind: 'vehicle'; id: number };

export function alertTarget(incident: Pick<ControlIncident, 'EntityType' | 'EntityId'>): AlertTarget | null {
  switch (incident.EntityType) {
    case 'Trip':
      return { kind: 'trip', tripId: incident.EntityId };
    case 'Booking':
      return { kind: 'booking', bookingId: incident.EntityId };
    case 'Driver':
      return { kind: 'driver', id: incident.EntityId };
    case 'Vehicle':
      return { kind: 'vehicle', id: incident.EntityId };
    default:
      return null;
  }
}

const INCIDENT_TYPE: Record<ControlIncident['Type'], string> = {
  get UnassignedTrip() { return t.control.uncovered; },
  get PartialBooking() { return t.control.incompleteBooking; },
  get UnavailabilityGap() { return t.control.gap; },
  get DriverNearLimit() { return t.control.nearLimit; },
  get VehicleUnavailable() { return t.control.vehicleUnavailable; },
  get ForcedAssignment() { return t.control.forcedAssignment; },
  get InsufficientSeats() { return t.control.fewSeats; },
};

export const incidentTypeText = (type: ControlIncident['Type']): string => INCIDENT_TYPE[type] ?? t.control.otherWarning;

export function relative(deadline: Date, now: Date): string {
  const min = Math.round((deadline.getTime() - now.getTime()) / 60_000);
  if (Math.abs(min) < 1) return t.control.now;
  const abs = Math.abs(min),
    hours = Math.floor(abs / 60),
    minutes = abs % 60;
  const txt = hours ? `${hours} h${minutes ? ` ${String(minutes).padStart(2, '0')}` : ''}` : `${minutes} min`;
  return min > 0 ? t.control.inTime(txt) : t.control.ago(txt);
}

export function impactText(incident: Pick<ControlIncident, 'AffectedTripCount' | 'AffectedSeats'>): string {
  if (incident.AffectedTripCount === 0) return t.control.noneAffected;
  return `${t.common.services(incident.AffectedTripCount)} · ${t.common.seatCount(incident.AffectedSeats)}`;
}

export const OUTCOME_TEXT: Record<string, string> = {
  get Assigned() { return t.common.assigned; },
  get RejectedCompliance() { return t.control.blockedByRules; },
  get RejectedDriver() { return t.control.blockedDriver; },
  get RejectedVehicle() { return t.control.blockedVehicle; },
  get RejectedSchedule() { return t.control.blockedClash; },
  get Forced() { return t.control.forcedWithReason; },
};
export const outcomeText = (outcome: string) => OUTCOME_TEXT[outcome] ?? outcome;
export type OutcomeGroup = 'assigned' | 'blocked' | 'forced';
export const outcomeGroup = (outcome: string): OutcomeGroup =>
  outcome === 'Assigned' ? 'assigned' : outcome === 'Forced' ? 'forced' : 'blocked';

const BLOCK_REASON: Record<string, string> = {
  get RejectedDriver() { return t.control.reasonDriver; },
  get RejectedCompliance() { return t.control.reasonRules; },
  get RejectedVehicle() { return t.control.reasonVehicle; },
  get RejectedSchedule() { return t.control.reasonSchedule; },
};
export const blockReason = (outcome: string) => BLOCK_REASON[outcome] ?? outcome;

export function activityText(item: ActivityItem, driver: (id: number) => string, vehicle: (id: number) => string): string {
  const service = item.TripLabel?.trim() || t.common.serviceOf(item.TripId);
  return t.control.activity(driver(item.DriverId), vehicle(item.VehicleId), service);
}

export const pct = (part: number, total: number) => (total > 0 ? Math.round((part / total) * 100) : 0);

function ageText(updatedAt: Date, seconds: number): string {
  if (seconds < 5) return t.control.rightNow;
  if (seconds < 60) return t.control.secondsAgo(seconds);
  if (seconds < 3600) return t.control.minutesAgo(Math.floor(seconds / 60));
  return t.control.atTime(formatTime(updatedAt));
}

export function freshness(updatedAt: Date, now: Date, staleAfterMs = STALE_AFTER_MS): { text: string; stale: boolean } {
  const elapsedMs = Math.max(0, now.getTime() - updatedAt.getTime());
  return { text: ageText(updatedAt, Math.floor(elapsedMs / 1000)), stale: elapsedMs > staleAfterMs };
}

/** Always an absolute value, never a percentage. */
export function trend(today: number, yesterday: number): { delta: number; text: string } {
  const delta = today - yesterday;
  if (yesterday === 0 && today > 0) return { delta, text: t.control.newToday };
  return { delta, text: delta === 0 ? t.control.sameAsYesterday : t.control.vsYesterday(`${delta > 0 ? '+' : '−'}${Math.abs(delta)}`) };
}

/** Presentation only: the backend decides severity, grouping and order. */
export type IncidentTier = 'critical' | 'high' | 'preventive' | 'notice';
export const TIER_TEXT: Record<IncidentTier, string> = {
  get critical() { return t.control.critical; },
  get high() { return t.control.highImpact; },
  get preventive() { return t.control.preventive; },
  get notice() { return t.common.warning; },
};
export function incidentTier(incident: Pick<ControlIncident, 'Severity' | 'Type'>): IncidentTier {
  if (incident.Severity === 'Urgent') return 'critical';
  switch (incident.Type) {
    case 'VehicleUnavailable':
    case 'UnavailabilityGap':
    case 'InsufficientSeats':
    case 'UnassignedTrip':
      return 'high';
    case 'DriverNearLimit':
    case 'ForcedAssignment':
      return 'preventive';
    default:
      return 'notice';
  }
}

export function operationStatus(light: string | null | undefined): { text: string; tone: 'ok' | 'warn' | 'danger'; light: string } {
  if (light === 'Red') return { text: t.control.urgentAction, tone: 'danger', light: t.control.red };
  if (light === 'Amber') return { text: t.control.withWarnings, tone: 'warn', light: t.control.amber };
  return { text: t.control.stable, tone: 'ok', light: t.control.green };
}

/**
 * When the last reload failed, the previous value is kept and marked as stale, instead of showing it as current or emptying the
 * module.
 */
export type Fresh<T> = { ok: true; value: T; at: Date; stale?: { since: Date; error: string } } | { ok: false; error: string };

export function merge<T>(previous: Fresh<T> | undefined, next: { ok: true; value: T } | { ok: false; error: string }, at: Date): Fresh<T> {
  if (next.ok) return { ok: true, value: next.value, at };
  if (previous?.ok) return { ok: true, value: previous.value, at: previous.at, stale: { since: previous.at, error: next.error } };
  return { ok: false, error: next.error };
}

export function staleText(since: Date, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - since.getTime()) / 60_000));
  if (minutes < 1) return t.control.staleUnderMinute;
  if (minutes < 60) return t.control.staleMinutes(minutes);
  return t.control.staleSince(formatTime(since));
}

export function hourIndex(dayStart: Date, now: Date): number {
  return Math.floor((now.getTime() - dayStart.getTime()) / 3_600_000);
}
