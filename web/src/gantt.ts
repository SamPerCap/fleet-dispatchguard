import { locale } from './language';
const DAY_MS = 24 * 60 * 60 * 1000;

/** The API returns dates without a zone (DateTimeKind.Unspecified) that are UTC. */
export function parseApiDate(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
}

export interface BarPosition {
  left: number;
  width: number;
  clippedStart: boolean;
  clippedEnd: boolean;
}

export function barPosition(start: Date, end: Date, dayStart: Date): BarPosition | null {
  const from = dayStart.getTime();
  const to = from + DAY_MS;
  if (end.getTime() <= from || start.getTime() >= to) return null;
  const clippedStart = Math.max(start.getTime(), from);
  const clippedEnd = Math.min(end.getTime(), to);
  return {
    left: ((clippedStart - from) / DAY_MS) * 100,
    width: ((clippedEnd - clippedStart) / DAY_MS) * 100,
    clippedStart: start.getTime() < from,
    clippedEnd: end.getTime() > to,
  };
}

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days, date.getHours(), date.getMinutes(), date.getSeconds());
}

/** Every time is shown in the zone of the company, whatever the zone of the browser. */
export const TIME_ZONE = 'Europe/Madrid';
export const formatTime = (date: Date) => date.toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });
/** With the date, for lists that span more than a week. */
export const formatDateTime = (date: Date) =>
  date.toLocaleString(locale(), {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: TIME_ZONE,
  });
export const formatDayTime = (date: Date) =>
  date.toLocaleString(locale(), { weekday: 'short', hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });
