import { describe, expect, it } from 'vitest';
import {
  addsUp,
  auditableRules,
  axisMarks,
  defaultFilters,
  engineParams,
  hourlySeries,
  hourlyText,
  KPIS,
  madridDate,
  narrowed,
  originText,
  periodText,
  rangeText,
  ruleIndex,
} from './engine';
import type { EngineCounts, EngineHour, RuleGroup } from './types';

const hour = (iso: string, accepted = 0, blocked = 0, forced = 0): EngineHour => ({
  Hour: iso,
  Accepted: accepted,
  Blocked: blocked,
  Forced: forced,
  Validations: accepted + blocked + forced,
});

describe('engineParams', () => {
  const base = defaultFilters(new Date('2026-11-22T10:00:00Z'), 'all');

  it('sends the Madrid day at noon UTC and only the filters in use', () => {
    expect(base.date).toBe('2026-11-22');
    expect(engineParams(base).toString()).toBe('date=2026-11-22T12%3A00%3A00Z&days=1');
  });
  it('adds base, result, rule, driver, vehicle, who and paging', () => {
    const query = engineParams(
      { ...base, scope: 3, kind: 'Blocked', rule: 'daily', driverId: 7, vehicleId: 0, by: 'Lucía', days: 7 },
      { page: 2, pageSize: 25 },
    );
    expect(Object.fromEntries(query)).toEqual({
      date: '2026-11-22T12:00:00Z',
      days: '7',
      baseId: '3',
      kind: 'Blocked',
      rule: 'daily',
      driverId: '7',
      vehicleId: '0',
      by: 'Lucía',
      page: '2',
      pageSize: '25',
    });
  });
  it('the curve ignores the result filter (its three series come from the same rows)', () => {
    expect(engineParams({ ...base, kind: 'Forced' }, {}, false).has('kind')).toBe(false);
  });
  it('origin: filters rows, KPIs and curve; an attempt without it is manual', () => {
    expect(engineParams({ ...base, origin: 'Recommended' }, {}, false).get('origin')).toBe('Recommended');
    expect(engineParams(base).has('origin')).toBe(false);
    expect(narrowed({ ...base, origin: 'Manual' })).toBe(true);
    expect([originText('Recommended'), originText('Manual'), originText(undefined), originText(null)]).toEqual([
      'Recomendado',
      'Manual',
      'Manual',
      'Manual',
    ]);
  });
  it('narrowed: only rule, driver, vehicle, who or origin', () => {
    expect(narrowed(base)).toBe(false);
    expect(narrowed({ ...base, kind: 'Blocked', days: 7, scope: 2 })).toBe(false);
    expect(narrowed({ ...base, driverId: 0 })).toBe(true);
  });
});

describe('madridDate', () => {
  it('uses the Madrid calendar day, not the UTC one', () => {
    expect(madridDate(new Date('2026-11-21T23:30:00Z'))).toBe('2026-11-22'); // 00:30 in Madrid
    expect(madridDate(new Date('2026-07-01T21:59:00Z'))).toBe('2026-07-01'); // 23:59 in Madrid (UTC+2)
  });
});

describe('KPIs', () => {
  const counts: EngineCounts = { Validations: 10, Accepted: 6, Blocked: 3, Forced: 1 };
  it('one KPI per result, in order, reading the counts', () => {
    expect(KPIS.map(k => k.kind)).toEqual(['All', 'Assigned', 'Blocked', 'Forced']);
    expect(KPIS.map(k => k.value(counts))).toEqual([10, 6, 3, 1]);
  });
  it('validations = accepted + blocked + forced', () => {
    expect(addsUp(counts)).toBe(true);
    expect(addsUp({ ...counts, Validations: 11 })).toBe(false);
  });
});

describe('hourlySeries', () => {
  const now = new Date('2026-11-22T11:30:00Z');
  const hours = [
    hour('2026-11-22T09:00:00', 2, 0, 1),
    hour('2026-11-22T10:00:00'),
    hour('2026-11-22T11:00:00', 0, 3),
    hour('2026-11-22T12:00:00', 4), // future: never drawn
  ];
  it('an hour without attempts or not started yet is a gap, not 0; zeros inside a data hour stay 0', () => {
    const series = hourlySeries(hours, now);
    expect(series.accepted).toEqual([2, null, 0, null]);
    expect(series.blocked).toEqual([0, null, 3, null]);
    expect(series.forced).toEqual([1, null, 0, null]);
    expect(series.withData).toBe(2);
  });
});

describe('axisMarks and hourlyText', () => {
  // Madrid day of 22 Nov 2026 (UTC+1): 23:00Z on the 21st → 23:00Z on the 22nd.
  const day = Array.from({ length: 24 }, (_, index) =>
    hour(new Date(Date.UTC(2026, 10, 21, 23 + index)).toISOString().slice(0, 19), index === 10 ? 5 : 0, index === 10 ? 1 : 0),
  );
  it('a day: every 6 h in Madrid time', () => {
    expect(axisMarks(day, 1)).toEqual([
      { index: 0, text: '00:00' },
      { index: 6, text: '06:00' },
      { index: 12, text: '12:00' },
      { index: 18, text: '18:00' },
    ]);
  });
  it('a week: one mark per Madrid midnight', () => {
    const week = Array.from({ length: 48 }, (_, index) => hour(new Date(Date.UTC(2026, 10, 20, 23 + index)).toISOString().slice(0, 19)));
    expect(axisMarks(week, 7).map(m => m.index)).toEqual([0, 24]);
  });
  it('describes totals by result, the peak and the gaps', () => {
    expect(hourlyText(day, 1)).toBe(
      'Validaciones por hora del día: 6 en total (5 aceptadas, 1 bloqueadas, 0 forzadas); máximo 6 a las 10:00. Las horas sin intentos aparecen como huecos.',
    );
    expect(hourlyText([hour('2026-11-22T09:00:00')], 7)).toBe('Validaciones por hora de los 7 días: ninguna.');
  });
});

describe('rangeText and periodText', () => {
  it('rows shown of the total', () => {
    expect(rangeText(1, 25, 80)).toBe('1–25 de 80');
    expect(rangeText(4, 25, 80)).toBe('76–80 de 80');
    expect(rangeText(1, 25, 0)).toBe('0 de 0');
  });
  it('one day or a week', () => {
    const to = new Date('2026-11-22T23:00:00Z');
    expect(periodText(new Date('2026-11-21T23:00:00Z'), to, 1)).toBe('domingo, 22 de noviembre');
    expect(periodText(new Date('2026-11-15T23:00:00Z'), to, 7)).toBe('del 16 al 22 de noviembre');
    expect(periodText(new Date('2026-10-26T23:00:00Z'), new Date('2026-11-02T23:00:00Z'), 7)).toBe('del 27 de octubre al 2 de noviembre');
  });
});

describe('rules', () => {
  const groups: RuleGroup[] = [
    {
      Key: 'driving',
      Title: 'Conducción',
      Rules: [
        { Id: 'daily', Name: 'Diaria', Reference: 'Art. 6.1', Explanation: '', Figure: '9 h', Kind: 'Blocking' },
        { Id: 'daily-extended', Name: 'Ampliado', Reference: 'Art. 6.1', Explanation: '', Figure: '10 h', Kind: 'Blocking' },
      ],
    },
    {
      Key: 'ranking',
      Title: 'Ranking',
      Rules: [{ Id: 'proximity', Name: 'Proximidad', Reference: '', Explanation: '', Figure: '', Kind: 'Ranking' }],
    },
  ];
  it('indexes rules by id with their group', () => {
    expect(ruleIndex(groups).get('proximity')?.group.Key).toBe('ranking');
  });
  it('the rule filter offers only what an attempt can carry, grouped, without empty groups', () => {
    const result = auditableRules(groups, ['daily']);
    expect(result).toHaveLength(1);
    expect(result[0].Rules.map(x => x.Id)).toEqual(['daily']);
  });
});
