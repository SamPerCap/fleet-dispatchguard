import { t } from './i18n/t';
// The figures (9 h, 10 h, 28 days) come from the API (`DrivingLimits`, `ResourceHistory.Days`): none is written here.
import type { DriverHours, ActivityKind, HistoryDay, ResourceHistory } from './types';
import { madridDate } from './engine';
import { parseApiDate, TIME_ZONE } from './gantt';
import type { ResourceKind } from './resourceUrl';
import { locale } from './language';

export type BarTone = 'normal' | 'extended' | 'over';
export function barTone(hours: number, daily: number, extended: number): BarTone {
  if (hours > extended) return 'over';
  if (hours > daily) return 'extended';
  return 'normal';
}

/** +1 so that the limit line does not touch the edge. */
export function chartCeiling(values: number[], mark = 0): number {
  const top = Math.max(0, ...values, mark > 0 ? mark + 1 : 0);
  return top > 0 ? Math.ceil(top) : 1;
}

const oneDecimal = (value: number) => value.toLocaleString(locale(), { maximumFractionDigits: 1 });
export const formatHours = (hours: number) => `${oneDecimal(Math.round(hours * 10) / 10)} h`;
/** "In progress" only when the API says so; its end date is then the date of the query. */
export const weeklyRestText = (hours: Pick<DriverHours, 'LastWeeklyRestEnd' | 'WeeklyRestInProgress'>, fmt: (instant: string) => string) =>
  hours.WeeklyRestInProgress
    ? t.history.restInProgress
    : hours.LastWeeklyRestEnd
      ? t.history.ended(fmt(hours.LastWeeklyRestEnd))
      : t.history.noRest;
export const formatDecimal = (value: number) => value.toLocaleString(locale(), { minimumFractionDigits: 1, maximumFractionDigits: 1 });
export const formatKm = (km: number) => `${oneDecimal(km)} km`;

const shortDate = (date: Date) => date.toLocaleDateString(locale(), { day: 'numeric', month: 'short', timeZone: TIME_ZONE });
const weekday = (date: Date) => date.toLocaleDateString(locale(), { weekday: 'short', timeZone: TIME_ZONE });

/** The API date is the UTC instant when the Madrid day starts. */
export const dayLabel = (day: HistoryDay) => {
  const date = parseApiDate(day.Date);
  return `${weekday(date)}, ${shortDate(date)}`;
};

export function dayMarks(days: HistoryDay[]): { index: number; text: string }[] {
  const marks = days.flatMap((day, index) => {
    const at = parseApiDate(day.Date);
    const isMonday = new Intl.DateTimeFormat('en-GB', { weekday: 'short', timeZone: TIME_ZONE }).format(at) === 'Mon';
    return isMonday && index < days.length - 1 ? [{ index, text: shortDate(at) }] : [];
  });
  return days.length ? [...marks, { index: days.length - 1, text: t.history.today }] : [];
}

export function historyText(history: ResourceHistory, kind: ResourceKind): string {
  const dayCount = history.DaysCount;
  const totals = history.Totals;
  if (totals.Trips === 0) return t.history.chartNone(dayCount);
  const peak = history.Days.reduce((best, day) => (day.Hours > best.Hours ? day : best), history.Days[0]);
  const base =
    t.history.chartBase(dayCount, formatHours(totals.Hours), kind === 'driver' ? t.history.ofDriving : t.history.ofUse, totals.Trips) +
    t.history.chartPeak(totals.DaysWorked, formatHours(peak.Hours), dayLabel(peak));
  if (kind === 'driver') {
    const daily = formatHours(history.DailyLimitHours),
      ext = formatHours(history.ExtendedDailyLimitHours);
    return (
      `${base} ${countText(totals.DaysOverDaily, t.history.overLimit(daily))}; ${countText(totals.DaysOverExtended, t.history.overLimit(ext))}.` +
      t.history.chartLines(daily, ext)
    );
  }
  return t.history.chartKm(base, formatKm(totals.Km), totals.TripsWithoutKm ? t.history.withoutKm(totals.TripsWithoutKm) : '');
}
const countText = (count: number, what: string) =>
  count === 0 ? t.history.noDay(what) : count === 1 ? t.history.oneDay(what) : t.history.manyDays(count, what);

export function auditParams(
  kind: ResourceKind,
  id: number,
  result: ActivityKind,
  page: number,
  pageSize: number,
  days: number,
  now: Date,
): URLSearchParams {
  const query = new URLSearchParams({
    date: `${madridDate(now)}T12:00:00Z`,
    days: String(days),
    page: String(page),
    pageSize: String(pageSize),
  });
  query.set(kind === 'driver' ? 'driverId' : 'vehicleId', String(id));
  if (result !== 'All') query.set('kind', result);
  return query;
}
