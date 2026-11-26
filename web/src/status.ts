import { t } from './i18n/t';
// Closed taxonomy and single source of the statuses. Without telemetry, "in progress" and "completed" are by schedule.
import { addDays, parseApiDate, startOfDay } from './gantt';

export type StatusKey = 'planned' | 'assigned' | 'running' | 'completed' | 'uncovered' | 'forced' | 'partial';
export const STATUS_KEYS: readonly StatusKey[] = ['planned', 'assigned', 'running', 'completed', 'uncovered', 'forced', 'partial'];

export const STATUS_TEXT: Record<StatusKey, { label: string; definition: string }> = {
  planned: { get label() { return t.status.planned; }, get definition() { return t.status.plannedWhat; } },
  assigned: { get label() { return t.common.assigned; }, get definition() { return t.status.assignedWhat; } },
  running: {
    get label() { return t.situation.running; },
    get definition() { return t.status.runningWhat; },
  },
  completed: { get label() { return t.status.completed; }, get definition() { return t.status.completedWhat; } },
  uncovered: { get label() { return t.status.uncovered; }, get definition() { return t.status.uncoveredWhat; } },
  forced: {
    get label() { return t.status.forced; },
    get definition() { return t.status.forcedWhat; },
  },
  partial: { get label() { return t.common.partial; }, get definition() { return t.status.partialWhat; } },
};

export interface TripLike {
  Start: string;
  End: string;
  Status: string;
  AssignedDriverId: number | null;
  AssignedVehicleId: number | null;
  ForcedDescription?: string | null;
}

export const isCovered = (trip: Pick<TripLike, 'AssignedDriverId' | 'AssignedVehicleId'>) =>
  trip.AssignedDriverId != null && trip.AssignedVehicleId != null;

/** The browser zone is Europe/Madrid in operation. */
const endOfToday = (now: Date) => addDays(startOfDay(now), 1);

/** Cancelled is not part of the taxonomy (null). Forced does not replace the time status: it is shown as well (see `isForced`). */
export function tripStatus(trip: TripLike, now: Date): StatusKey | null {
  if (trip.Status === 'Cancelled') return null;
  if (trip.Status === 'Completed') return 'completed';
  const start = parseApiDate(trip.Start),
    end = parseApiDate(trip.End);
  if (!isCovered(trip)) return start >= endOfToday(now) ? 'planned' : 'uncovered';
  if (now < start) return 'assigned';
  return now < end ? 'running' : 'completed';
}

export const isForced = (trip: Pick<TripLike, 'ForcedDescription'>) => !!trip.ForcedDescription;

export function bookingStatus(assignment: string, start: string | null, now: Date): StatusKey | null {
  switch (assignment) {
    case 'FullyAssigned':
      return 'assigned';
    case 'PartiallyAssigned':
      return 'partial';
    case 'Unassigned':
      return start && parseApiDate(start) >= endOfToday(now) ? 'planned' : 'uncovered';
    default:
      return null;
  }
}
