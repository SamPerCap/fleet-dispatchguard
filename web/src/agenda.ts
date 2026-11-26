import { t } from './i18n/t';
import { createDrafts } from './drafts';
import { parseApiDate } from './gantt';
import { isMadridMidnight, madridDayStart, madridInstant, madridMoment, shiftDate } from './madridTime';
import { isOver, overlappingUnavailabilities } from './resources';
import type { ResourceKind } from './resourceUrl';
import type { TripLike } from './status';
import type { Unavailability } from './types';

export const MAX_NOTES = 500;
export const MAX_PROVIDER = 200;

/** In the order they are offered; the first one is the default. The names are in `resources.ts:UNAVAILABILITY_LABEL`. */
export const AGENDA_TYPES: Record<ResourceKind, readonly string[]> = {
  driver: ['Vacation', 'SickLeave', 'WeeklyRest', 'Training', 'PersonalLeave', 'Other'],
  vehicle: ['TechnicalInspection', 'Maintenance', 'Repair', 'Cleaning', 'Administrative', 'Other'],
};

const TYPE_HINT: Record<string, string> = {
  get SickLeave() { return t.agenda.noMedicalData; },
  get WeeklyRest() { return t.agenda.onlyBlocks; },
};
export const typeHint = (type: string): string | null => TYPE_HINT[type] ?? null;

/** As typed: dates and times of Madrid. With whole days, `endDate` is the last day included and the times do not count. */
export interface AgendaForm {
  type: string;
  wholeDays: boolean;
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;
  notes: string;
  provider: string;
}

export function newForm(kind: ResourceKind, now: Date): AgendaForm {
  const { date } = madridMoment(now);
  return {
    type: AGENDA_TYPES[kind][0],
    wholeDays: kind === 'driver', // Holidays are whole days; an ITV is a couple of hours.
    startDate: date,
    startTime: '08:00',
    endDate: date,
    endTime: '10:00',
    notes: '',
    provider: '',
  };
}

export const isWholeDays = (entry: Pick<Unavailability, 'Start' | 'End'>): boolean =>
  isMadridMidnight(parseApiDate(entry.Start)) && isMadridMidnight(parseApiDate(entry.End));

export function formOf(entry: Unavailability): AgendaForm {
  const start = madridMoment(parseApiDate(entry.Start));
  const end = madridMoment(parseApiDate(entry.End));
  const wholeDays = isWholeDays(entry);
  return {
    type: entry.Type,
    wholeDays,
    startDate: start.date,
    startTime: start.time,
    endDate: wholeDays ? shiftDate(end.date, -1) : end.date,
    endTime: end.time,
    notes: entry.Notes ?? '',
    provider: entry.ServiceProvider ?? '',
  };
}

/** Moving the start past the end drags the end along. */
export const withStartDate = (form: AgendaForm, startDate: string): AgendaForm => ({
  ...form,
  startDate,
  endDate: startDate && form.endDate < startDate ? startDate : form.endDate,
});

export type Period = { ok: true; start: Date; end: Date } | { ok: false; error: string };

/** A date field lets the year be typed digit by digit, and also more than four. */
const isPlausible = (date: string) => /^\d{4}-\d{2}-\d{2}$/.test(date) && date >= '2000-01-01' && date <= '2099-12-31';


/**
 * An end of the period that was not touched keeps its instant: typed back, one inside the repeated hour of October or with seconds
 * would move.
 */
export function periodOf(form: AgendaForm, original?: Unavailability): Period {
  const before = original && formOf(original);
  const sameMode = before?.wholeDays === form.wholeDays;
  if (!form.startDate || !form.endDate) return { ok: false, error: t.agenda.chooseDates };
  if (!isPlausible(form.startDate) || !isPlausible(form.endDate)) return { ok: false, error: t.agenda.badDate };
  if (!form.wholeDays && (!form.startTime || !form.endTime)) return { ok: false, error: t.agenda.chooseTimes };
  if (form.wholeDays && form.endDate < form.startDate) return { ok: false, error: t.agenda.lastBeforeFirst };

  const startKept = sameMode && before.startDate === form.startDate && (form.wholeDays || before.startTime === form.startTime);
  const endKept = sameMode && before.endDate === form.endDate && (form.wholeDays || before.endTime === form.endTime);
  const typedStart = form.wholeDays ? madridDayStart(form.startDate) : madridInstant(form.startDate, form.startTime);
  const typedEnd = form.wholeDays ? madridDayStart(shiftDate(form.endDate, 1)) : madridInstant(form.endDate, form.endTime);
  const start = startKept ? parseApiDate(original!.Start) : typedStart;
  const end = endKept ? parseApiDate(original!.End) : typedEnd;
  if (!start) return { ok: false, error: t.agenda.startNoSuchTime(t.agenda.noSuchTime) };
  if (!end) return { ok: false, error: t.agenda.endNoSuchTime(t.agenda.noSuchTime) };
  if (end <= start) return { ok: false, error: t.agenda.endAfterStart };
  return { ok: true, start, end };
}

export interface AgendaInput {
  Start: string;
  End: string;
  Type: string;
  Notes: string | null;
  ServiceProvider?: string | null;
}

export const inputOf = (kind: ResourceKind, form: AgendaForm, period: { start: Date; end: Date }): AgendaInput => ({
  Start: period.start.toISOString(),
  End: period.end.toISOString(),
  Type: form.type,
  Notes: form.notes.trim() || null,
  ...(kind === 'vehicle' ? { ServiceProvider: form.provider.trim() || null } : {}),
});

const dateText = (date: string) => date.split('-').reverse().join('/');

export function periodText(entry: Pick<Unavailability, 'Start' | 'End'>): string {
  const start = madridMoment(parseApiDate(entry.Start));
  const end = madridMoment(parseApiDate(entry.End));
  if (isWholeDays(entry)) {
    const last = shiftDate(end.date, -1);
    return start.date === last
      ? t.agenda.allDay(dateText(start.date))
      : t.agenda.wholeDaysRange(dateText(start.date), dateText(last));
  }
  return start.date === end.date
    ? `${dateText(start.date)}, ${start.time} – ${end.time}`
    : `${dateText(start.date)}, ${start.time} – ${dateText(end.date)}, ${end.time}`;
}

export interface Caught<T> {
  /** A person can still do something about these. */
  open: T[];
  finished: T[];
}

/** What an entry catches. The criterion is the one of the engine, in `overlappingUnavailabilities`. */
export function caughtBy<T extends TripLike>(entry: Unavailability, trips: T[], now: Date): Caught<T> {
  const caught = trips.filter(trip => trip.Status !== 'Cancelled' && overlappingUnavailabilities(trip, [entry]).length > 0);
  return splitByFinished(caught, now);
}

export const splitByFinished = <T extends Pick<TripLike, 'End' | 'Status'>>(trips: T[], now: Date): Caught<T> => ({
  open: trips.filter(trip => !isOver(trip, now)),
  finished: trips.filter(trip => isOver(trip, now)),
});

export const affectedText = (count: number) => t.agenda.conflicts(count);

/** Covers every entry, so a single request answers for all of them. */
export function spanOf(entries: Pick<Unavailability, 'Start' | 'End'>[]): { start: Date; end: Date } | null {
  if (entries.length === 0) return null;
  return {
    start: new Date(Math.min(...entries.map(entry => parseApiDate(entry.Start).getTime()))),
    end: new Date(Math.max(...entries.map(entry => parseApiDate(entry.End).getTime()))),
  };
}

export interface AgendaDraft {
  /** Null: a new entry. */
  entryId: number | null;
  form: AgendaForm;
}

const drafts = createDrafts<AgendaDraft>();
export const draftKey = (kind: ResourceKind, id: number) => `${kind}-${id}`;
export const readDraft = drafts.read;
export const writeDraft = drafts.write;

/** A draft of an entry that was deleted or that ended meanwhile cannot be saved. */
export const usableDraft = (draft: AgendaDraft | null, entries: Unavailability[], now: Date): AgendaDraft | null => {
  if (!draft || draft.entryId == null) return draft;
  const entry = entries.find(candidate => candidate.Id === draft.entryId);
  return entry && parseApiDate(entry.End) > now ? draft : null;
};
