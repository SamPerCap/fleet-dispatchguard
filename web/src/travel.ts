import { t } from './i18n/t';
/** The backend provides the figure; here it is only shown. */

export type TravelTimeSource = 'None' | 'Historical' | 'Estimated';
export interface TravelTime {
  Minutes: number | null;
  Source: TravelTimeSource;
  Samples: number;
}

const toMinutes = (hhmm: string): number | null => {
  const match = /^(\d{2}):(\d{2})$/.exec(hhmm);
  return match ? Number(match[1]) * 60 + Number(match[2]) : null;
};

/** null without a time or past midnight: the form does not cross days. */
export function addMinutes(start: string, minutes: number): string | null {
  const startMinutes = toMinutes(start);
  if (startMinutes === null || minutes <= 0) return null;
  const total = startMinutes + minutes;
  if (total >= 24 * 60) return null;
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return hours > 0 ? `${hours} h ${String(remainingMinutes).padStart(2, '0')} min` : `${remainingMinutes} min`;
}

export function suggestedEnd(start: string, endTouched: boolean, travel: TravelTime | undefined): string | null {
  if (endTouched || !travel || travel.Minutes === null || travel.Source === 'None') return null;
  return addMinutes(start, travel.Minutes);
}

/** Never blocks. */
export function travelWarning(travel: TravelTime | undefined, from: string, to: string, start: string, end: string): string | null {
  if (!travel || travel.Minutes === null || travel.Source === 'None') return null;
  const startMinutes = toMinutes(start),
    endMinutes = toMinutes(end);
  if (startMinutes === null || endMinutes === null || endMinutes <= startMinutes) return null;
  const given = endMinutes - startMinutes;
  if (given >= travel.Minutes) return null;
  const head =
    travel.Source === 'Historical'
      ? t.travel.recorded(from, to, formatDuration(travel.Minutes), travel.Samples)
      : t.travel.estimated(from, to, formatDuration(travel.Minutes));
  return t.travel.youPut(head, formatDuration(given));
}
