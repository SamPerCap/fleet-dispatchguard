import { describe, expect, it } from 'vitest';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import {
  createReportLoader,
  dateParam,
  departedText,
  isOutdated,
  kmText,
  savingText,
  visibleReport,
  weekOf,
  type ReportRequest,
  type ReportState,
} from './optimize';
import { OptimizePanel, type KmReport } from './OptimizePanel';
import type { VehicleChaining } from './types';
import { Stat } from './ui';

describe('weekOf', () => {
  it('goes from Monday to Monday when the day is a Sunday', () => {
    const { from, to } = weekOf(new Date(2026, 10, 22, 15));
    expect(dateParam(from)).toBe('2026-11-16');
    expect(dateParam(to)).toBe('2026-11-23');
  });
  it('a Monday starts that same day', () => {
    expect(dateParam(weekOf(new Date(2026, 10, 16, 8)).from)).toBe('2026-11-16');
  });
});

const week = (returnToBase: boolean, from = '2026-11-23', changes = '0.0'): ReportRequest => ({
  from,
  to: '2026-11-30',
  returnToBase,
  changes,
});
/** What `OptimizeSheet` keeps while the report is open. */
const fakeReport = (state: ReportState): KmReport => ({
  state,
  returnToBase: true,
  setReturnToBase: () => undefined,
  load: async () => undefined,
  place: { current: null },
});
const report = (km: number) => ({ BaselineDeadheadKm: km }) as VehicleChaining;

/** Its responses are delivered by hand, in the order each test needs. */
function manual() {
  const pending: { request: ReportRequest; ok: (report: VehicleChaining) => void; fail: (error: Error) => void }[] = [];
  const states: ReportState[] = [];
  const load = createReportLoader(
    request =>
      new Promise((ok, fail) => {
        pending.push({ request, ok, fail });
      }),
    s => states.push(s),
  );
  return { pending, states, load, last: () => states[states.length - 1] };
}

describe('report: figures of the current request', () => {
  it('a late response does not overwrite the most recent one, nor the other way round', async () => {
    const loader = manual();
    const first = loader.load(week(true));
    const second = loader.load(week(false));
    loader.pending[1].ok(report(2));
    await second;
    loader.pending[0].ok(report(1));
    await first;
    expect(loader.last()).toEqual({ status: 'done', request: week(false), result: report(2) });
  });
  it('neither does a late error', async () => {
    const loader = manual();
    const first = loader.load(week(true));
    const second = loader.load(week(false));
    loader.pending[1].ok(report(2));
    await second;
    loader.pending[0].fail(new Error('tarde'));
    await first;
    expect(loader.last().status).toBe('done');
  });
  it('recalculating removes the previous figures', async () => {
    const loader = manual();
    const first = loader.load(week(true));
    loader.pending[0].ok(report(1));
    await first;
    void loader.load(week(false));
    expect(loader.last()).toEqual({ status: 'loading', request: week(false) });
  });
  it('a failure is shown as a failure', async () => {
    const loader = manual();
    const run = loader.load(week(true));
    loader.pending[0].fail(new Error('sin red'));
    await run;
    expect(loader.last()).toEqual({ status: 'failed', request: week(true), error: new Error('sin red') });
  });
  it('without a requested report, changing the checkbox calculates nothing', () => {
    expect(isOutdated({ status: 'idle' }, week(false))).toBe(false);
    expect(visibleReport({ status: 'idle' }, week(false))).toEqual({ status: 'idle' });
  });
  it('a report of another checkbox or another week is not shown: it reads as calculating', () => {
    const done: ReportState = { status: 'done', request: week(true), result: report(1) };
    expect(visibleReport(done, week(true))).toBe(done);
    for (const other of [week(false), week(true, '2026-11-16')]) {
      expect(isOutdated(done, other)).toBe(true);
      expect(visibleReport(done, other)).toEqual({ status: 'loading', request: other });
    }
    expect(visibleReport({ status: 'failed', request: week(true), error: new Error('x') }, week(false)).status).toBe('loading');
  });
});

describe('report: figures of what is stored now', () => {
  const done: ReportState = { status: 'done', request: week(true), result: report(1) };
  const after = week(true, undefined, '0.1');
  it('after a stored change the previous figures are not shown: it must recalculate', () => {
    expect(isOutdated(done, after)).toBe(true);
    expect(visibleReport(done, after)).toEqual({ status: 'loading', request: after });
  });
  it('nor with an assignment in progress, even if the request is the same', () => {
    expect(visibleReport(done, week(true), true)).toEqual({ status: 'loading', request: week(true) });
    expect(visibleReport(done, week(true), false)).toBe(done);
  });
  it('an earlier failure does not show again while saving', () => {
    expect(visibleReport({ status: 'failed', request: week(true), error: new Error('x') }, week(true), true).status).toBe('loading');
  });
  it('without a requested report, saving something calculates nothing', () => {
    expect(isOutdated({ status: 'idle' }, after)).toBe(false);
    expect(visibleReport({ status: 'idle' }, after, true)).toEqual({ status: 'idle' });
  });
});

describe('report: opening a service', () => {
  const result = {
    TripsConsidered: 2,
    TripsDeparted: 0,
    BaselineDeadheadKm: 100,
    ProposedDeadheadKm: 60,
    SavingPercent: 40,
    AverageSpeedKmh: 70,
    Explanation: { Summary: '', Business: [], Flow: [], Patterns: [], Adrs: [] },
    Changes: [
      {
        TripId: 45,
        Start: '2026-11-24T09:00:00Z',
        End: '2026-11-24T10:00:00Z',
        CurrentVehicleId: 70,
        CurrentLicensePlate: '7000AAA',
        ProposedVehicleId: 99,
        ProposedLicensePlate: '9900ZZZ',
      },
    ],
  } as unknown as VehicleChaining;
  const day = new Date(2026, 10, 24);
  const { from, to } = weekOf(day);
  const request: ReportRequest = { from: dateParam(from), to: dateParam(to), returnToBase: true, changes: '0.0' };
  const html = (state: ReportState) =>
    renderToStaticMarkup(
      createElement(OptimizePanel, {
        day,
        assignName: 'Asignar semana',
        version: 0,
        openPanel: () => undefined,
        report: fakeReport(state),
      }),
    );
  it('the number of each service is a button with its own name', () => {
    expect(html({ status: 'done', request, result })).toMatch(
      /<button[^>]*aria-label="Abrir el servicio #45 para ver o cambiar su autobús"[^>]*>#45<\/button>/,
    );
  });
  it('with figures from before the last change there is neither table nor km', () => {
    const old = html({ status: 'done', request: { ...request, changes: '9.0' }, result });
    expect(old).not.toContain('#45');
    expect(old).not.toContain('100,0');
  });
});

describe('report: how the figures read', () => {
  it('without changes there is no saving, never "0,0 %"', () => {
    expect(savingText(0, 0)).toBe('Sin ahorro');
    expect(savingText(3, 55.9)).toBe('55,9 %');
  });
  it('with changes, a saving that rounds to zero is said', () => {
    expect(savingText(1, 0.04)).toBe('menos del 0,1 %');
    expect(savingText(1, 0.05)).toBe('0,1 %');
    expect(savingText(1, 0.4, 0)).toBe('menos del 1 %');
  });
  it('thousands take a separator also in four-digit figures', () => {
    expect(kmText(5517.9)).toBe('5.517,9');
    expect(kmText(12345.64)).toBe('12.345,6');
    expect(kmText(999)).toBe('999,0');
    expect(kmText(1234, 0)).toBe('1.234');
  });
  it('says how many have already left', () => {
    expect(departedText(231, 0)).toBe('');
    expect(departedText(231, 1)).toBe('1 ya ha salido');
    expect(departedText(231, 120)).toBe('120 ya han salido');
    expect(departedText(231, 231)).toBe('ya han salido todos');
    expect(departedText(1, 1)).toBe('ya ha salido');
  });
  it('cards do not cut their texts', () => {
    const html = renderToStaticMarkup(
      createElement(Stat, { label: 'Km en vacío propuestos', value: '5.517,9', foot: '207 cambios de autobús' }),
    );
    expect(html).toContain('207 cambios de autobús');
    expect(html).not.toContain('truncate');
  });
  it('the report text does not limit its width and says what happens with those that have already left', () => {
    const html = renderToStaticMarkup(
      createElement(OptimizePanel, {
        day: new Date(2026, 10, 24),
        assignName: 'Asignar semana',
        version: 0,
        openPanel: () => undefined,
        report: fakeReport({ status: 'idle' }),
      }),
    );
    expect(html).not.toMatch(/class="[^"]*\b(measure|max-w-)/);
    expect(html).toContain('Los que ya han salido conservan su autobús');
    expect(html).not.toContain('incluidos los que ya han salido');
  });
});
