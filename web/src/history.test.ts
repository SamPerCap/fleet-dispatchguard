import { describe, expect, it } from 'vitest';
import { auditParams, barTone, chartCeiling, dayMarks, formatHours, formatKm, historyText, weeklyRestText } from './history';
import type { HistoryDay, ResourceHistory } from './types';

// 28 Madrid days ending on Sunday 22 Nov 2026 (CET): each day starts at 23:00Z of the previous one.
const days = (hours: number[] = []): HistoryDay[] =>
  Array.from({ length: 28 }, (_, index) => ({
    Date: new Date(Date.UTC(2026, 9, 25, 23) + index * 86_400_000).toISOString().replace('Z', ''),
    Hours: hours[index] ?? 0,
    Trips: hours[index] ? 1 : 0,
    Km: 0,
  }));
const history = (over: Partial<ResourceHistory> = {}): ResourceHistory => ({
  Resource: 'Driver',
  ResourceId: 1,
  At: '2026-11-22T09:00:00',
  From: '2026-10-25T23:00:00',
  To: '2026-11-22T09:00:00',
  DaysCount: 28,
  DailyLimitHours: 9,
  ExtendedDailyLimitHours: 10,
  MaxExtendedDaysPerWeek: 2,
  Days: days(),
  Totals: { Hours: 0, Trips: 0, Km: 0, TripsWithoutKm: 0, DaysWorked: 0, DaysOverDaily: 0, DaysOverExtended: 0 },
  Items: [],
  Page: 1,
  PageSize: 20,
  TotalCount: 0,
  TotalPages: 0,
  ...over,
});

describe('barTone', () => {
  it('exactly 9 h is normal; above it, extended; above 10 h, over', () => {
    expect(barTone(0, 9, 10)).toBe('normal');
    expect(barTone(9, 9, 10)).toBe('normal');
    expect(barTone(9.25, 9, 10)).toBe('extended');
    expect(barTone(10, 9, 10)).toBe('extended');
    expect(barTone(10.5, 9, 10)).toBe('over');
  });
});

describe('chartCeiling', () => {
  it('leaves room for the limit line and rounds up', () => {
    expect(chartCeiling([2, 5], 10)).toBe(11);
    expect(chartCeiling([12.3], 10)).toBe(13);
    expect(chartCeiling([3.2])).toBe(4);
    expect(chartCeiling([])).toBe(1);
  });
});

describe('formats', () => {
  it('hours and km with a decimal comma and at most one decimal', () => {
    expect(formatHours(9.5)).toBe('9,5 h');
    expect(formatHours(11.25)).toBe('11,3 h');
    expect(formatHours(0)).toBe('0 h');
    expect(formatKm(67.3)).toBe('67,3 km');
    expect(formatKm(1234.5)).toBe('1234,5 km');
  });
});

describe('dayMarks', () => {
  it('marks the Mondays and the last day as "hoy"', () => {
    const marks = dayMarks(days());
    expect(marks[marks.length - 1]).toEqual({ index: 27, text: 'hoy' });
    // 26 Oct, 2 Nov, 9 Nov and 16 Nov are Mondays: indexes 0, 7, 14 and 21.
    expect(marks.slice(0, -1).map(m => m.index)).toEqual([0, 7, 14, 21]);
  });
  it('no days, no marks', () => expect(dayMarks([])).toEqual([]));
});

describe('historyText', () => {
  it('without services it says so', () => {
    expect(historyText(history(), 'driver')).toBe('Últimos 28 días: sin servicios hechos.');
  });
  it('driver: totals, peak and days above the limits, with the API figures', () => {
    const sample = history({
      Days: days([0, 9.5, 4]),
      Totals: { Hours: 13.5, Trips: 2, Km: 0, TripsWithoutKm: 0, DaysWorked: 2, DaysOverDaily: 1, DaysOverExtended: 0 },
    });
    const text = historyText(sample, 'driver');
    expect(text).toContain('13,5 h de conducción en 2 servicios y 2 días con servicio');
    expect(text).toContain('máximo 9,5 h el mar, 27 oct');
    expect(text).toContain('1 día por encima de 9 h; ningún día por encima de 10 h');
  });
  it('vehicle: estimated km and services without km', () => {
    const sample = history({
      Resource: 'Vehicle',
      Days: days([3]),
      Totals: { Hours: 3, Trips: 2, Km: 134.6, TripsWithoutKm: 1, DaysWorked: 1, DaysOverDaily: 0, DaysOverExtended: 0 },
    });
    expect(historyText(sample, 'vehicle')).toContain('134,6 km estimados en línea recta; 1 servicio sin km estimables');
  });
});

describe('auditParams', () => {
  it('28 days ending today in Madrid, by driver or vehicle, with the outcome unless it is All', () => {
    const now = new Date('2026-11-22T23:30:00Z'); // already 23 Nov in Madrid
    const driverParams = auditParams('driver', 12, 'All', 2, 15, 28, now);
    expect(driverParams.get('date')).toBe('2026-11-23T12:00:00Z');
    expect(driverParams.get('days')).toBe('28');
    expect(driverParams.get('driverId')).toBe('12');
    expect(driverParams.has('vehicleId')).toBe(false);
    expect(driverParams.has('kind')).toBe(false);
    expect(driverParams.get('page')).toBe('2');
    const vehicleParams = auditParams('vehicle', 7, 'Blocked', 1, 15, 28, now);
    expect(vehicleParams.get('vehicleId')).toBe('7');
    expect(vehicleParams.get('kind')).toBe('Blocked');
  });
});

describe('weeklyRestText', () => {
  const fmt = (text: string) => `<${text}>`;
  it('in progress: does not say that it ended', () =>
    expect(weeklyRestText({ LastWeeklyRestEnd: 'ahora', WeeklyRestInProgress: true }, fmt)).toBe(
      'descanso semanal en curso, según horario',
    ));
  it('ended: with its date', () =>
    expect(weeklyRestText({ LastWeeklyRestEnd: 'x', WeeklyRestInProgress: false }, fmt)).toBe('terminó <x>'));
  it('no known rest', () =>
    expect(weeklyRestText({ LastWeeklyRestEnd: null, WeeklyRestInProgress: false }, fmt)).toBe(
      'sin descanso semanal en el historial: cuenta toda la ventana',
    ));
});
