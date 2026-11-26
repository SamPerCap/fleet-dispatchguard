import { t } from './i18n/t';
// The backend decides the status; here it is only counted, ordered and explained.
import { isUnassigned } from './calendar';
import { formatTime, parseApiDate, TIME_ZONE } from './gantt';
import { madridMoment } from './madridTime';
import type { BookingTrip } from './types';
import { locale } from './language';

/** Its place in the booking, counting the cancelled ones: its number does not change when another one is cancelled. */
export interface NumberedTrip {
  trip: BookingTrip;
  ordinal: number;
  total: number;
}

export interface TripDay {
  /** Madrid day of departure, `YYYY-MM-DD`. */
  date: string;
  start: Date;
  trips: NumberedTrip[];
}

export interface BookingSummary {
  active: number;
  covered: number;
  noDriver: number;
  noVehicle: number;
  cancelled: number;
  maxSeats: number;
  reasons: string[];
  nextUncovered: NumberedTrip | null;
}

const instant = (value: string) => parseApiDate(value).getTime();

function numbered(trips: BookingTrip[]): NumberedTrip[] {
  return [...trips]
    .sort((a, b) => instant(a.Start) - instant(b.Start) || instant(a.End) - instant(b.End) || a.Id - b.Id)
    .map((trip, index) => ({ trip, ordinal: index + 1, total: trips.length }));
}

export function tripDays(trips: BookingTrip[]): TripDay[] {
  const days: TripDay[] = [];
  for (const item of numbered(trips)) {
    const start = parseApiDate(item.trip.Start);
    const { date } = madridMoment(start);
    const last = days[days.length - 1];
    if (last?.date === date) last.trips.push(item);
    else days.push({ date, start, trips: [item] });
  }
  return days;
}

export const tripName = ({ ordinal, total }: Pick<NumberedTrip, 'ordinal' | 'total'>) => t.bookingPanel.tripOf(ordinal, total);

export function bookingSummary(trips: BookingTrip[]): BookingSummary {
  const active = trips.filter(t => t.Status !== 'Cancelled');
  const open = numbered(trips).filter(({ trip }) => isUnassigned(trip));
  const reasons = open.map(({ trip, ordinal }) => {
    const lacksBoth = trip.AssignedDriverId == null && trip.AssignedVehicleId == null;
    const lacksOne = trip.AssignedDriverId == null ? t.common.driverLower : t.common.vehicleLower;
    return t.bookingPanel.missingIn(ordinal, trip.Id, lacksBoth ? t.bookingPanel.driverAndVehicle : lacksOne);
  });
  const cancelled = trips.length - active.length;
  if (active.length === 0) reasons.push(trips.length === 0 ? t.bookingPanel.hasNoTrips : t.bookingPanel.allCancelled);
  else if (open.length === 0) reasons.push(t.bookingPanel.allCovered);
  return {
    active: active.length,
    covered: active.length - open.length,
    noDriver: open.filter(({ trip }) => trip.AssignedDriverId == null).length,
    noVehicle: open.filter(({ trip }) => trip.AssignedVehicleId == null).length,
    cancelled,
    maxSeats: active.reduce((max, trip) => Math.max(max, trip.RequiredSeats), 0),
    reasons,
    nextUncovered: open.find(({ trip }) => !trip.IsLocked) ?? null,
  };
}

export interface CoveragePart {
  text: string;
  tone: 'neutral' | 'covered' | 'missing';
}


export function coverage({ active, covered, noDriver, noVehicle, cancelled }: BookingSummary): CoveragePart[] {
  const parts: CoveragePart[] = [{ text: t.common.trips(active + cancelled), tone: 'neutral' }];
  if (active > 0)
    parts.push({ text: t.bookingPanel.coveredOf(covered, active), tone: covered === active ? 'covered' : 'neutral' });
  if (noDriver > 0) parts.push({ text: t.bookingPanel.withoutDriver(noDriver), tone: 'missing' });
  if (noVehicle > 0) parts.push({ text: t.bookingPanel.withoutVehicle(noVehicle), tone: 'missing' });
  if (cancelled > 0) parts.push({ text: t.bookingPanel.cancelledCount(cancelled), tone: 'neutral' });
  return parts;
}

/** What to say where «Asignar el siguiente» would be; null if there is a next one. */
export function pendingNote({ active, covered, nextUncovered }: BookingSummary): string | null {
  if (nextUncovered) return null;
  if (active === 0) return t.bookingPanel.nothingToAssign;
  return covered < active
    ? t.bookingPanel.leftIsLocked
    : t.bookingPanel.nothingPending;
}

const dateText = (date: Date, options: Intl.DateTimeFormatOptions) =>
  date.toLocaleDateString(locale(), { ...options, timeZone: TIME_ZONE });
const SHORT_DAY: Intl.DateTimeFormatOptions = { weekday: 'short', day: 'numeric', month: 'short' };

export const dayText = (date: Date) => dateText(date, SHORT_DAY);
export const longDayText = (date: Date) => dateText(date, { weekday: 'long', day: 'numeric', month: 'long' });

const sameMadridDay = (start: Date, end: Date) => madridMoment(start).date === madridMoment(end).date;

export const arrivalDayText = (start: Date, end: Date): string | null => (sameMadridDay(start, end) ? null : t.bookingPanel.arrivesOn(dayText(end)));

const MINUTE = 60_000;

/** By the instants, so it holds when the clock changes on the way. */
export function durationText(start: Date, end: Date): string {
  const total = Math.round((end.getTime() - start.getTime()) / MINUTE);
  const hours = Math.floor(total / 60);
  const minutes = total % 60;
  return [hours > 0 && `${hours} h`, (minutes > 0 || hours === 0) && `${minutes} min`].filter(Boolean).join(' ');
}

/** The times on screen do not add up to the duration. */
export function clockChanges(start: Date, end: Date): boolean {
  const wallClock = (date: Date) => {
    const { date: day, time } = madridMoment(date);
    return Date.parse(`${day}T${time}:00Z`);
  };
  const minuteOf = (date: Date) => Math.floor(date.getTime() / MINUTE) * MINUTE;
  return wallClock(end) - wallClock(start) !== minuteOf(end) - minuteOf(start);
}

export function dateRangeText(start: Date, end: Date): string {
  const day = (date: Date) => dateText(date, { ...SHORT_DAY, year: 'numeric' });
  return sameMadridDay(start, end)
    ? `${day(start)} · ${formatTime(start)} – ${formatTime(end)}`
    : `${day(start)}, ${formatTime(start)} – ${day(end)}, ${formatTime(end)}`;
}
