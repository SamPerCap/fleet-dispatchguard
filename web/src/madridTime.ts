import { TIME_ZONE } from './gantt';

/** What a date field and a time field hold: `YYYY-MM-DD` and `HH:mm`, in Madrid whatever the zone of the browser. */
export interface LocalMoment {
  date: string;
  time: string;
}

const MINUTE = 60_000;
const DAY = 24 * 60 * MINUTE;
const MIDNIGHT = '00:00';

const format = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

export function madridMoment(instant: Date): LocalMoment {
  const parts = Object.fromEntries(format.formatToParts(instant).map(part => [part.type, part.value]));
  return { date: `${parts.year.padStart(4, '0')}-${parts.month}-${parts.day}`, time: `${parts.hour}:${parts.minute}` };
}

const asIfUtc = ({ date, time }: LocalMoment): number => Date.parse(`${date}T${time}:00Z`);

const offsetAt = (instant: number): number =>
  asIfUtc(madridMoment(new Date(instant))) - Math.floor(instant / MINUTE) * MINUTE;

/**
 * Null when that day has no such time (the clock skips 02:00–03:00 in March) or the field is not a date. The hour that happens twice
 * in October is the first one.
 */
export function madridInstant(date: string, time: string): Date | null {
  const wanted = asIfUtc({ date, time });
  if (!Number.isFinite(wanted)) return null;
  // A change of hour is at most one day away from both probes, so they see the two possible offsets.
  const offsets = [offsetAt(wanted - DAY), offsetAt(wanted + DAY)].sort((a, b) => b - a);
  for (const offset of offsets) {
    const candidate = new Date(wanted - offset);
    const shown = madridMoment(candidate);
    if (shown.date === date && shown.time === time) return candidate;
  }
  return null;
}

/** Midnight is never skipped nor repeated in Madrid. */
export const madridDayStart = (date: string): Date | null => madridInstant(date, MIDNIGHT);

export function shiftDate(date: string, days: number): string {
  return new Date(Date.parse(`${date}T00:00:00Z`) + days * DAY).toISOString().slice(0, 10);
}

export const isMadridMidnight = (instant: Date): boolean => madridMoment(instant).time === MIDNIGHT && instant.getTime() % MINUTE === 0;
