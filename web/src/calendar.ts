import { t } from './i18n/t';
import { addDays, parseApiDate, startOfDay, TIME_ZONE } from './gantt';
import { isForced } from './status';
import type { BookingTrip } from './types';
import { locale } from './language';

export type Scale = 'day' | 'week' | 'month';
export interface Range {
  from: Date;
  to: Date;
  days: Date[];
}

export function startOfWeek(date: Date): Date {
  const start = startOfDay(date);
  return addDays(start, -((start.getDay() + 6) % 7)); // Monday
}

function boundsOf(anchor: Date, scale: Scale): { from: Date; to: Date } {
  if (scale === 'day') {
    const from = startOfDay(anchor);
    return { from, to: addDays(from, 1) };
  }
  if (scale === 'week') {
    const from = startOfWeek(anchor);
    return { from, to: addDays(from, 7) };
  }
  return {
    from: new Date(anchor.getFullYear(), anchor.getMonth(), 1),
    to: new Date(anchor.getFullYear(), anchor.getMonth() + 1, 1),
  };
}

/** The week starts on Monday. */
export function rangeOf(anchor: Date, scale: Scale): Range {
  const { from, to } = boundsOf(anchor, scale);
  const days: Date[] = [];
  for (let day = from; day < to; day = addDays(day, 1)) days.push(day);
  return { from, to, days };
}

export function shift(anchor: Date, scale: Scale, dir: 1 | -1): Date {
  if (scale === 'day') return addDays(startOfDay(anchor), dir);
  if (scale === 'week') return addDays(startOfDay(anchor), 7 * dir);
  return new Date(anchor.getFullYear(), anchor.getMonth() + dir, 1);
}

export interface Span {
  left: number;
  width: number;
}

export function spanIn(start: Date, end: Date, range: Range): Span | null {
  const from = range.from.getTime();
  const to = range.to.getTime();
  if (end.getTime() <= from || start.getTime() >= to) return null;
  const clippedStart = Math.max(start.getTime(), from);
  const clippedEnd = Math.min(end.getTime(), to);
  return { left: ((clippedStart - from) / (to - from)) * 100, width: ((clippedEnd - clippedStart) / (to - from)) * 100 };
}

export const isUnassigned = (trip: Pick<BookingTrip, 'AssignedDriverId' | 'AssignedVehicleId' | 'Status'>) =>
  trip.Status !== 'Cancelled' && trip.Status !== 'Completed' && (trip.AssignedDriverId == null || trip.AssignedVehicleId == null);

export const overlaps = (trip: Pick<BookingTrip, 'Start' | 'End'>, range: Range) =>
  spanIn(parseApiDate(trip.Start), parseApiDate(trip.End), range) !== null;

export function rangeLabel(range: Range, scale: Scale): string {
  const format = (date: Date, options: Intl.DateTimeFormatOptions) => date.toLocaleDateString(locale(), { ...options, timeZone: TIME_ZONE });
  if (scale === 'day') return format(range.from, { weekday: 'long', day: 'numeric', month: 'short', year: 'numeric' });
  if (scale === 'month') return format(range.from, { month: 'long', year: 'numeric' });
  const last = addDays(range.to, -1);
  return `${format(range.from, { day: 'numeric', month: 'short' })} – ${format(last, { day: 'numeric', month: 'short', year: 'numeric' })}`;
}

export type Tone = 'ok' | 'warn' | 'danger' | 'muted';
/** Unknown values fall back to the raw value. */
export const SERVICE_TYPES = ['Regular', 'Discretionary', 'Circuit', 'AirportTransfer'] as const;
const SERVICE_TYPE_LABEL: Record<string, string> = {
  get Regular() { return t.common.regular; },
  get Discretionary() { return t.common.discretionary; },
  get Circuit() { return t.common.circuit; },
  get AirportTransfer() { return t.common.airportTransfer; },
};
export const serviceTypeLabel = (serviceType: string): string => SERVICE_TYPE_LABEL[serviceType] ?? serviceType;

/** Covered = driver and vehicle; forced = marked as forced, by itself or by another service (SPECs 0056 and 0063). */
export function planningStats(trips: Pick<BookingTrip, 'AssignedDriverId' | 'AssignedVehicleId' | 'Status' | 'ForcedDescription'>[]) {
  const live = trips.filter(t => t.Status !== 'Cancelled');
  const covered = live.filter(t => t.AssignedDriverId != null && t.AssignedVehicleId != null).length;
  return {
    total: live.length,
    covered,
    coveredPct: live.length ? Math.round((covered / live.length) * 100) : 0,
    pending: live.filter(isUnassigned).length,
    forced: live.filter(t => isForced(t)).length,
  };
}
