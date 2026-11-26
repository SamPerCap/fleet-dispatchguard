import { t } from './i18n/t';
import { createLatestGuard } from './latest';
import type { VehicleChaining } from './types';
import { locale } from './language';

export function weekOf(day: Date): { from: Date; to: Date } {
  const from = new Date(day.getFullYear(), day.getMonth(), day.getDate());
  from.setDate(from.getDate() - ((from.getDay() + 6) % 7));
  const to = new Date(from);
  to.setDate(to.getDate() + 7);
  return { from, to };
}

/** The API takes it as midnight in Madrid, also when the clocks change (SPEC 0071). */
export function dateParam(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** `changes` identifies what was stored when it was requested: if it changes, the report no longer applies. */
export interface ReportRequest {
  from: string;
  to: string;
  returnToBase: boolean;
  changes: string;
}
export type ReportState =
  | { status: 'idle' }
  | { status: 'loading'; request: ReportRequest }
  | { status: 'done'; request: ReportRequest; result: VehicleChaining }
  | { status: 'failed'; request: ReportRequest; error: Error };

const same = (first: ReportRequest, second: ReportRequest) =>
  first.from === second.from && first.to === second.to && first.returnToBase === second.returnToBase && first.changes === second.changes;

export const isOutdated = (state: ReportState, request: ReportRequest): boolean => state.status !== 'idle' && !same(state.request, request);

/** What belongs to another request, or with an assignment in progress, counts as "calculating". */
export const visibleReport = (state: ReportState, request: ReportRequest, saving = false): ReportState =>
  isOutdated(state, request) || (saving && state.status !== 'idle') ? { status: 'loading', request } : state;

/** Each request clears the previous one and only the last one reaches `onState` (a late response is discarded). */
export function createReportLoader(
  fetchReport: (request: ReportRequest) => Promise<VehicleChaining>,
  onState: (state: ReportState) => void,
) {
  const guard = createLatestGuard();
  return async (request: ReportRequest): Promise<void> => {
    const isCurrent = guard.begin();
    onState({ status: 'loading', request });
    try {
      const result = await fetchReport(request);
      if (isCurrent()) onState({ status: 'done', request, result });
    } catch (cause) {
      if (isCurrent()) onState({ status: 'failed', request, error: cause as Error });
    }
  };
}

const number = (value: number, decimals: number) =>
  value.toLocaleString(locale(), { minimumFractionDigits: decimals, maximumFractionDigits: decimals, useGrouping: true });

/** With a thousands separator also in four-digit figures. */
export const kmText = (value: number, decimals = 1) => number(value, decimals);

/** Without changes, or without km to save, there is no saving; with changes it never says "0 %". */
export function savingText(changes: number, percent: number, decimals = 1): string {
  if (changes === 0) return t.optimize.noSaving;
  const step = 10 ** -decimals;
  return percent < step / 2 ? t.optimize.lessThan(number(step, decimals)) : `${number(percent, decimals)} %`;
}

export function departedText(total: number, departed: number): string {
  if (departed === 0) return '';
  if (departed === total) return total === 1 ? t.optimize.oneDeparted : t.optimize.allDeparted;
  return departed === 1 ? t.optimize.departedOne : t.optimize.departed(number(departed, 0));
}
