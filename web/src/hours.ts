import { t } from './i18n/t';
import type { DriverHours } from './types';

export const hourLimits = (hours: Pick<DriverHours, 'Today' | 'ThisWeek' | 'LastTwoWeeks' | 'MaxDaily' | 'MaxWeekly' | 'MaxBiWeekly'>) => [
  { label: t.hours.day, value: hours.Today, max: hours.MaxDaily },
  { label: t.hours.week, value: hours.ThisWeek, max: hours.MaxWeekly },
  { label: t.hours.twoWeeks, value: hours.LastTwoWeeks, max: hours.MaxBiWeekly },
];

/** One figure per limit: the stricter of the two clocks (ADR 0017). */
export const hoursNote = () => t.hours.what;
