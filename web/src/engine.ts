import { t } from './i18n/t';
import type { Scope } from './api';
import { formatTime, parseApiDate, TIME_ZONE } from './gantt';
import type { ActivityKind, AttemptOrigin, EngineCounts, EngineHour, Rule, RuleGroup } from './types';
import { locale } from './language';

/** The chosen operational day, or the week that ends on it. */
export type PeriodDays = 1 | 7;

/** `kind` filters rows; the KPIs and the curve use all the others. */
export interface EngineFilters {
  /** Operational day in Europe/Madrid, `YYYY-MM-DD`. */
  date: string;
  days: PeriodDays;
  scope: Scope;
  kind: ActivityKind;
  rule: string | null;
  driverId: number | null;
  vehicleId: number | null;
  by: string | null;
  origin: AttemptOrigin | null;
}

export const PAGE_SIZE = 25;

export function madridDate(now: Date): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
}

export function defaultFilters(now: Date, scope: Scope): EngineFilters {
  return { date: madridDate(now), days: 1, scope, kind: 'All', rule: null, driverId: null, vehicleId: null, by: null, origin: null };
}

/**
 * The date goes at noon UTC: it always falls inside the Madrid day (UTC+1/+2), so the server picks that operational day and not the
 * previous one. `withKind = false` for the curve, whose three series come from the same rows.
 */
export function engineParams(filters: EngineFilters, extra: { page?: number; pageSize?: number } = {}, withKind = true): URLSearchParams {
  const query = new URLSearchParams({ date: `${filters.date}T12:00:00Z`, days: String(filters.days) });
  if (filters.scope !== 'all') query.set('baseId', String(filters.scope));
  if (withKind && filters.kind !== 'All') query.set('kind', filters.kind);
  if (filters.rule) query.set('rule', filters.rule);
  if (filters.driverId != null) query.set('driverId', String(filters.driverId));
  if (filters.vehicleId != null) query.set('vehicleId', String(filters.vehicleId));
  if (filters.by) query.set('by', filters.by);
  if (filters.origin) query.set('origin', filters.origin);
  if (extra.page != null) query.set('page', String(extra.page));
  if (extra.pageSize != null) query.set('pageSize', String(extra.pageSize));
  return query;
}

export const narrowed = (filters: EngineFilters) =>
  filters.rule != null || filters.driverId != null || filters.vehicleId != null || filters.by != null || filters.origin != null;

/** Attempts without an origin are older, or made by hand. */
export const originText = (origin: AttemptOrigin | null | undefined) => (origin === 'Recommended' ? t.common.recommended : t.common.manual);

export const KPIS: { kind: ActivityKind; label: string; value: (counts: EngineCounts) => number }[] = [
  { kind: 'All', get label() { return t.engine.validations; }, value: c => c.Validations },
  { kind: 'Assigned', get label() { return t.engine.accepted; }, value: c => c.Accepted },
  { kind: 'Blocked', get label() { return t.engine.blocked; }, value: c => c.Blocked },
  { kind: 'Forced', get label() { return t.engine.forced; }, value: c => c.Forced },
];

/** API invariant: validations = accepted + blocked + forced. */
export const addsUp = (counts: EngineCounts) => counts.Validations === counts.Accepted + counts.Blocked + counts.Forced;

/** An hour without attempts, or that has not started, is a gap (null), not a 0. */
export function hourlySeries(
  hours: EngineHour[],
  now: Date,
): { accepted: (number | null)[]; blocked: (number | null)[]; forced: (number | null)[]; withData: number } {
  const gap = (hour: EngineHour) => hour.Validations === 0 || parseApiDate(hour.Hour).getTime() > now.getTime();
  const pick = (field: 'Accepted' | 'Blocked' | 'Forced') => hours.map(h => (gap(h) ? null : h[field]));
  return { accepted: pick('Accepted'), blocked: pick('Blocked'), forced: pick('Forced'), withData: hours.filter(h => !gap(h)).length };
}

const madridHour = (date: Date) =>
  Number(new Intl.DateTimeFormat('en-GB', { timeZone: TIME_ZONE, hour: '2-digit', hourCycle: 'h23' }).format(date));
const shortDay = (date: Date) => date.toLocaleDateString(locale(), { weekday: 'short', day: 'numeric', timeZone: TIME_ZONE });

export function axisMarks(hours: EngineHour[], days: PeriodDays): { index: number; text: string }[] {
  if (hours.length === 0) return [];
  return hours.flatMap((hour, index) => {
    const at = parseApiDate(hour.Hour);
    const local = madridHour(at);
    if (days === 1) return local % 6 === 0 ? [{ index, text: formatTime(at) }] : [];
    return local === 0 ? [{ index, text: shortDay(at) }] : [];
  });
}

export function hourlyText(hours: EngineHour[], days: PeriodDays): string {
  const sum = (field: 'Validations' | 'Accepted' | 'Blocked' | 'Forced') => hours.reduce((subtotal, hour) => subtotal + hour[field], 0);
  const total = sum('Validations');
  const period = days === 1 ? t.engine.ofTheDay : t.engine.ofSevenDays;
  if (total === 0) return t.engine.hourlyNone(period);
  const peak = hours.reduce((best, hour) => (hour.Validations > best.Validations ? hour : best), hours[0]);
  const at = parseApiDate(peak.Hour);
  const when = days === 1 ? t.control.atTime(formatTime(at)) : t.engine.onDayAt(shortDay(at), formatTime(at));
  return (
    t.engine.hourly(period, total, sum('Accepted'), sum('Blocked'), sum('Forced')) +
    ' ' +
    t.engine.hourlyPeak(peak.Validations, when)
  );
}

export function rangeText(page: number, pageSize: number, total: number): string {
  if (total === 0) return t.engine.zeroOfZero;
  const first = (page - 1) * pageSize + 1;
  return t.engine.rangeOf(Math.min(first, total), Math.min(page * pageSize, total), total);
}

export function periodText(from: Date, to: Date, days: PeriodDays): string {
  const last = new Date(to.getTime() - 1);
  const long = (date: Date) => date.toLocaleDateString(locale(), { weekday: 'long', day: 'numeric', month: 'long', timeZone: TIME_ZONE });
  if (days === 1) return long(last);
  const day = (date: Date) => date.toLocaleDateString(locale(), { day: 'numeric', timeZone: TIME_ZONE });
  const month = (date: Date) => date.toLocaleDateString(locale(), { month: 'long', timeZone: TIME_ZONE });
  return month(from) === month(last)
    ? t.engine.periodSameMonth(day(from), day(last), month(last))
    : t.engine.periodTwoMonths(day(from), month(from), day(last), month(last));
}

export function ruleIndex(groups: RuleGroup[]): Map<string, { rule: Rule; group: RuleGroup }> {
  return new Map(groups.flatMap(group => group.Rules.map(r => [r.Id, { rule: r, group }] as const)));
}

/** `ids`: the rules an attempt can carry, as the API says. */
export function auditableRules(groups: RuleGroup[], ids: string[]): RuleGroup[] {
  const set = new Set(ids);
  return groups.map(group => ({ ...group, Rules: group.Rules.filter(r => set.has(r.Id)) })).filter(g => g.Rules.length > 0);
}
