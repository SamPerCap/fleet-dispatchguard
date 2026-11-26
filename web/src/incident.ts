import { addDays, parseApiDate, startOfDay } from './gantt';
import type { TimeWindow, Trip } from './types';

const CLOSED = new Set(['Cancelled', 'Completed']);

/** The overlap is [Start, End) against [start, end). */
export function affectedTrips(trips: Trip[], driverId: number, leave: TimeWindow): Trip[] {
  return trips
    .filter(t => t.AssignedDriverId === driverId && t.IsActive && !CLOSED.has(t.Status))
    .filter(t => parseLocal(t.Start) < leave.end && parseLocal(t.End) > leave.start)
    .sort((a, b) => parseLocal(a.Start).getTime() - parseLocal(b.Start).getTime());
}

/** From now (or from the start of the day shown, if it is in the future) to the end of that day. */
export function defaultLeaveWindow(day: Date, now: Date): TimeWindow {
  const dayStart = startOfDay(day);
  const end = addDays(dayStart, 1);
  return { start: now > dayStart && now < end ? now : dayStart, end };
}

const parseLocal = parseApiDate;
